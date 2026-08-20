using EAIOS.Api.Application.Identity;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Email;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Authentication : login, refresh, logout, register, vérification email,
/// réinitialisation de mot de passe, MFA.
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController(
    IUserRepository          userRepo,
    ISessionRepository       sessionRepo,
    IInvitationRepository    invitationRepo,
    IMfaCredentialRepository mfaRepo,
    IUserRoleRepository      userRoleRepo,
    ITokenService            tokenService,
    IPasswordService         passwordService,
    ITotpService             totpService,
    IPermissionService       permissionService,
    IEmailService            emailService,
    IAnalyticsTracker        analytics,
    IConfiguration           configuration,
    ILogger<AuthController>  logger) : V1ApiController
{
    // ═════════════════════════════════════════════════════════════════════════
    // LOGIN / REFRESH / LOGOUT
    // ═════════════════════════════════════════════════════════════════════════

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(req.Email.Trim().ToUpperInvariant(), ct);

        if (user == null || !passwordService.VerifyPassword(req.Password, user.PasswordHash ?? ""))
        {
            await Task.Delay(300, ct); // Délai constant : limite les attaques temporelles
            if (user != null)
            {
                user.RecordFailedLogin(
                    configuration.GetValue("Security:MaxFailedLoginAttempts", 5),
                    configuration.GetValue("Security:LockoutDurationMinutes", 15));
                await userRepo.SaveAsync(ct);
            }
            return Unauthorized(new { code = "INVALID_CREDENTIALS", message = "Email ou mot de passe incorrect." });
        }

        // Le verrouillage après échecs répétés doit bloquer même un mot de passe correct.
        if (user.IsLockedOut)
            return StatusCode(423, new
            {
                code = "ACCOUNT_LOCKED",
                message = "Compte temporairement verrouillé après trop de tentatives.",
                lockedUntil = user.LockedUntil
            });

        if (user.Status == UserStatus.Suspended)
            return StatusCode(403, new { code = "ACCOUNT_SUSPENDED", message = user.SuspensionReason });

        if (user.Status == UserStatus.Deactivated)
            return StatusCode(403, new { code = "ACCOUNT_DEACTIVATED", message = "Ce compte a été désactivé." });

        if (user.Status == UserStatus.PendingVerification)
            return StatusCode(403, new
            {
                code = "EMAIL_NOT_VERIFIED",
                message = "Vérifiez votre adresse email avant de vous connecter."
            });

        // A partir d'ici l'utilisateur est authentifie : on adopte son organisation
        // pour que MFA, session et roles soient lus dans le bon tenant.
        AdoptTenant(user.OrganizationId);

        // ── Palier MFA ────────────────────────────────────────────────────────
        if (user.IsMfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(req.MfaCode))
            {
                return Ok(new MfaChallengeResponse(
                    RequiresMfa: true,
                    MfaToken:    tokenService.GenerateRefreshToken(),
                    Methods:     (user.MfaMethods ?? "Totp").Split(',', StringSplitOptions.RemoveEmptyEntries)));
            }

            var mfaCred = await mfaRepo.FindByUserAndMethodAsync(user.Id, MfaMethod.Totp, ct);
            if (mfaCred == null || !totpService.VerifyCode(mfaCred.SecretEncrypted ?? "", req.MfaCode))
                return Unauthorized(new { code = "INVALID_MFA_CODE", message = "Code MFA invalide." });

            mfaCred.RecordUse();
            await mfaRepo.SaveAsync(ct);
        }

        var (roles, permissions) = await ResolveGrantsAsync(user, ct);

        var sessionId = Guid.CreateVersion7();
        var tokenPair = tokenService.Issue(user.Id, user.OrganizationId, sessionId, roles, permissions);

        var lifetimeDays = req.RememberMe
            ? configuration.GetValue("Security:RefreshTokenLifetimeDays", 7) * 4
            : configuration.GetValue("Security:RefreshTokenLifetimeDays", 7);

        var session = Session.Create(
            user.OrganizationId, user.Id,
            tokenPair.RefreshTokenHash,
            lifetimeDays,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString());

        await sessionRepo.AddAsync(session, ct);
        user.RecordLogin(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        await userRepo.SaveAsync(ct);
        await sessionRepo.SaveAsync(ct);

        await analytics.TrackAsync(AnalyticsEventTypes.UserLoggedIn,
            resourceId: user.Id, resourceType: "User", ct: ct);

        return Ok200(new LoginResponse(
            AccessToken:  tokenPair.AccessToken,
            RefreshToken: tokenPair.RefreshToken,
            ExpiresIn:    (int)(tokenPair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            User:         MapUser(user, roles, permissions)));
    }

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        var hash    = tokenService.HashRefreshToken(req.RefreshToken);
        var session = await sessionRepo.FindByRefreshTokenHashAsync(hash, ct);

        if (session == null || !session.IsValid)
            return Unauthorized(new { code = "INVALID_REFRESH_TOKEN", message = "Session expirée ou révoquée." });

        AdoptTenant(session.OrganizationId);

        var user = await userRepo.GetByIdAsync(session.UserId, ct);
        if (user == null || user.Status != UserStatus.Active)
            return Unauthorized(new { code = "USER_NOT_FOUND_OR_INACTIVE" });

        // Les rôles et permissions doivent être ré-émis : les omettre priverait
        // l'utilisateur de tous ses droits dès le premier rafraîchissement.
        var (roles, permissions) = await ResolveGrantsAsync(user, ct);

        var newPair = tokenService.Issue(user.Id, user.OrganizationId, session.Id, roles, permissions);

        session.RotateRefreshToken(newPair.RefreshTokenHash);
        session.RecordActivity();
        await sessionRepo.SaveAsync(ct);

        return Ok200(new RefreshResponse(
            newPair.AccessToken,
            newPair.RefreshToken,
            (int)(newPair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
        {
            var hash    = tokenService.HashRefreshToken(req.RefreshToken);
            var session = await sessionRepo.FindByRefreshTokenHashAsync(hash, ct);
            if (session != null)
            {
                session.Revoke("user_logout");
                await sessionRepo.SaveAsync(ct);
            }
        }
        else if (ActorId.HasValue)
        {
            // Sans refresh token fourni, on ferme toutes les sessions de l'utilisateur.
            await sessionRepo.RevokeAllForUserAsync(ActorId.Value, "user_logout", ct);
            await sessionRepo.SaveAsync(ct);
        }

        return NoContent204();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INSCRIPTION & VÉRIFICATION EMAIL
    // ═════════════════════════════════════════════════════════════════════════

    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var invitation = await invitationRepo.FindByTokenAsync(req.InvitationToken, ct);
        if (invitation == null || !invitation.IsValid)
            return BadRequest(new { code = "INVALID_INVITATION", message = "Invitation invalide ou expirée." });

        // L'invitation vaut pour une adresse précise : accepter une autre adresse
        // permettrait de détourner l'invitation de quelqu'un d'autre.
        var normalizedEmail = req.Email.Trim().ToUpperInvariant();
        if (!string.Equals(invitation.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            return BadRequest(new
            {
                code = "EMAIL_MISMATCH",
                message = "Cette invitation a été émise pour une autre adresse email."
            });

        if (!passwordService.IsStrongPassword(req.Password))
            return BadRequest(new { code = "WEAK_PASSWORD", message = "Le mot de passe ne respecte pas les critères de sécurité." });

        // L'invitation porte l'organisation cible.
        AdoptTenant(invitation.OrganizationId);

        if (await userRepo.EmailExistsAsync(normalizedEmail, ct))
            return Conflict("Un compte avec cet email existe déjà.");

        var user = Domain.Identity.User.Create(invitation.OrganizationId, req.Email.Trim(), req.FirstName, req.LastName);
        user.SetPasswordHash(passwordService.HashPassword(req.Password));

        // L'adresse est déjà prouvée : l'invitation y a été envoyée et son jeton
        // vient d'être présenté. On active donc directement le compte.
        user.SetEmailVerificationToken(invitation.Token, expiryHours: 1);
        user.VerifyEmail(invitation.Token);

        await userRepo.AddAsync(user, ct);
        await userRepo.SaveAsync(ct);

        // Rôle prévu par l'invitation.
        if (invitation.RoleId.HasValue)
        {
            var assignment = Domain.AccessControl.UserRole.Create(
                invitation.OrganizationId, user.Id, invitation.RoleId.Value,
                invitation.Role ?? "org.member", invitation.InvitedBy);
            await userRoleRepo.AddAsync(assignment, ct);
            await userRoleRepo.SaveAsync(ct);
        }

        invitation.Accept(user.Id);
        invitationRepo.Update(invitation);
        await invitationRepo.SaveAsync(ct);

        await emailService.SendWelcomeAsync(user.Email, user.FirstName, "EAIOS", ct);

        logger.LogInformation("Nouvel utilisateur inscrit : {Email} ({UserId})", user.Email, user.Id);

        var (roles, permissions) = await ResolveGrantsAsync(user, ct);
        return Ok200(MapUser(user, roles, permissions));
    }

    /// <summary>Confirme l'adresse email à partir du jeton reçu et active le compte.</summary>
    [HttpPost("verify-email"), AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(req.Email.Trim().ToUpperInvariant(), ct);
        if (user == null)
            return BadRequest(new { code = "INVALID_TOKEN", message = "Lien de vérification invalide ou expiré." });

        AdoptTenant(user.OrganizationId);

        if (user.IsEmailVerified)
            return Ok200(new { message = "Cette adresse email est déjà vérifiée.", alreadyVerified = true });

        if (!user.VerifyEmail(req.Token))
            return BadRequest(new { code = "INVALID_TOKEN", message = "Lien de vérification invalide ou expiré." });

        userRepo.Update(user);
        await userRepo.SaveAsync(ct);

        await emailService.SendWelcomeAsync(user.Email, user.FirstName, "EAIOS", ct);

        logger.LogInformation("Email vérifié pour {UserId}", user.Id);
        return Ok200(new { message = "Adresse email vérifiée. Vous pouvez maintenant vous connecter.", verified = true });
    }

    /// <summary>Renvoie un lien de vérification. Réponse volontairement neutre.</summary>
    [HttpPost("resend-verification"), AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(req.Email.Trim().ToUpperInvariant(), ct);

        if (user is { IsEmailVerified: false })
        {
            AdoptTenant(user.OrganizationId);

            var token = GenerateSecureToken();
            user.SetEmailVerificationToken(token,
                configuration.GetValue("Security:EmailVerificationTokenLifetimeHours", 24));

            userRepo.Update(user);
            await userRepo.SaveAsync(ct);

            await emailService.SendEmailVerificationAsync(user.Email, user.FirstName, token, ct);
        }

        // Même réponse quelle que soit l'existence du compte : ne pas révéler
        // quelles adresses sont enregistrées.
        return Ok200(new { message = "Si un compte non vérifié correspond à cette adresse, un email vient d'être envoyé." });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MOT DE PASSE OUBLIÉ
    // ═════════════════════════════════════════════════════════════════════════

    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(req.Email.Trim().ToUpperInvariant(), ct);

        if (user is not null && user.Status != UserStatus.Deactivated)
        {
            AdoptTenant(user.OrganizationId);

            var token = GenerateSecureToken();
            user.SetPasswordResetToken(token,
                configuration.GetValue("Security:PasswordResetTokenLifetimeHours", 1));

            userRepo.Update(user);
            await userRepo.SaveAsync(ct);

            await emailService.SendPasswordResetAsync(user.Email, user.FirstName, token, ct);
            logger.LogInformation("Réinitialisation de mot de passe demandée pour {UserId}", user.Id);
        }

        // Réponse identique dans tous les cas : l'endpoint ne doit pas servir
        // à énumérer les comptes existants.
        return Ok200(new { message = "Si un compte correspond à cette adresse, un email de réinitialisation vient d'être envoyé." });
    }

    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(req.Email.Trim().ToUpperInvariant(), ct);

        if (user == null || !user.ValidatePasswordResetToken(req.Token))
        {
            await Task.Delay(300, ct);
            return BadRequest(new { code = "INVALID_RESET_TOKEN", message = "Lien de réinitialisation invalide ou expiré." });
        }

        if (!passwordService.IsStrongPassword(req.NewPassword))
            return BadRequest(new { code = "WEAK_PASSWORD", message = "Le mot de passe ne respecte pas les critères de sécurité." });

        AdoptTenant(user.OrganizationId);

        user.SetPasswordHash(passwordService.HashPassword(req.NewPassword));
        user.ClearPasswordResetToken();

        // Le compte était peut-être verrouillé par les tentatives ayant motivé la réinitialisation.
        if (user.Status == UserStatus.Active || user.IsLockedOut)
            user.Activate();

        userRepo.Update(user);
        await userRepo.SaveAsync(ct);

        // Un mot de passe changé doit invalider les sessions ouvertes ailleurs.
        await sessionRepo.RevokeAllForUserAsync(user.Id, "password_reset", ct);
        await sessionRepo.SaveAsync(ct);

        logger.LogInformation("Mot de passe réinitialisé pour {UserId} — sessions révoquées.", user.Id);
        return Ok200(new { message = "Mot de passe réinitialisé. Reconnectez-vous avec vos nouveaux identifiants." });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MFA
    // ═════════════════════════════════════════════════════════════════════════

    [HttpPost("mfa/setup"), Authorize]
    public async Task<IActionResult> MfaSetup(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound("Utilisateur introuvable.");

        var secret      = totpService.GenerateSecret();
        var qrUri       = totpService.BuildQrCodeUri(user.Email, secret);
        var backupCodes = totpService.GenerateBackupCodes();

        return Ok200(new MfaSetupDto(secret, qrUri, backupCodes));
    }

    [HttpPost("mfa/enable"), Authorize]
    public async Task<IActionResult> MfaEnable([FromBody] EnableTotpRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound("Utilisateur introuvable.");

        if (user.IsMfaEnabled)
            return Conflict("Le MFA est déjà activé sur ce compte.");

        if (!totpService.VerifyCode(req.Secret, req.Code))
            return BadRequest(new { code = "INVALID_TOTP_CODE", message = "Code de vérification invalide." });

        var cred = MfaCredential.Create(user.OrganizationId, user.Id, MfaMethod.Totp, req.Secret);
        var backupHashes = req.BackupCodes.Select(totpService.HashBackupCode).ToArray();
        cred.Activate(System.Text.Json.JsonSerializer.Serialize(backupHashes));

        await mfaRepo.AddAsync(cred, ct);
        user.EnableMfa(MfaMethod.Totp);
        userRepo.Update(user);
        await userRepo.SaveAsync(ct);
        await mfaRepo.SaveAsync(ct);

        logger.LogInformation("MFA TOTP activé pour {UserId}", user.Id);
        return Ok200(new { message = "MFA TOTP activé avec succès." });
    }

    [HttpPost("mfa/disable"), Authorize]
    public async Task<IActionResult> MfaDisable([FromBody] DisableMfaRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound("Utilisateur introuvable.");

        if (!passwordService.VerifyPassword(req.Password, user.PasswordHash ?? ""))
            return Unauthorized(new { code = "INVALID_PASSWORD", message = "Mot de passe incorrect." });

        user.DisableMfa(MfaMethod.Totp);
        var creds = await mfaRepo.GetActiveByUserAsync(user.Id, ct);
        foreach (var c in creds) mfaRepo.SoftDelete(c);

        userRepo.Update(user);
        await userRepo.SaveAsync(ct);
        await mfaRepo.SaveAsync(ct);

        logger.LogInformation("MFA désactivé pour {UserId}", user.Id);
        return Ok200(new { message = "MFA désactivé." });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Rôles et permissions effectifs de l'utilisateur, embarqués dans le token.
    /// Le compte administrateur de démarrage conserve son rôle plateforme.
    /// </summary>
    private async Task<(string[] Roles, string[] Permissions)> ResolveGrantsAsync(Domain.Identity.User user, CancellationToken ct)
    {
        var assignments = await userRoleRepo.GetByUserAsync(user.Id, ct);
        var roles = assignments
            .Where(a => !a.IsExpired)
            .Select(a => a.RoleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bootstrapAdmin = configuration["Security:BootstrapAdminEmail"] ?? "admin@eaios.io";
        if (user.Email.Equals(bootstrapAdmin, StringComparison.OrdinalIgnoreCase)
            && !roles.Contains(Domain.AccessControl.SystemRoles.PlatformAdmin, StringComparer.OrdinalIgnoreCase))
        {
            roles.Add(Domain.AccessControl.SystemRoles.PlatformAdmin);
        }

        if (roles.Count == 0)
            roles.Add(Domain.AccessControl.SystemRoles.OrgMember);

        var permissions = await permissionService.GetEffectivePermissionsAsync(user.Id, ct);
        return (roles.ToArray(), permissions);
    }

    /// <summary>
    /// Adopte l'organisation de l'entite qui vient d'etre resolue de maniere
    /// cross-tenant. Les endpoints d'authentification sont anonymes : sans cette
    /// bascule, tous les acces suivants dans la meme requete (sessions, roles, MFA)
    /// seraient filtres sur un tenant vide et ne verraient rien.
    /// </summary>
    private void AdoptTenant(Guid organizationId)
    {
        if (organizationId != Guid.Empty && !TenantCtx.IsResolved)
            TenantCtx.SetTenant(organizationId);
    }

    private static string GenerateSecureToken() =>
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static UserDto MapUser(Domain.Identity.User u, IReadOnlyList<string>? roles = null, IReadOnlyList<string>? permissions = null) =>
        new(u.Id, u.OrganizationId, u.Email, u.FirstName, u.LastName, u.FullName, u.DisplayName,
            u.AvatarUrl, u.JobTitle, u.Department, u.Locale, u.TimeZone, u.Status, u.IsEmailVerified,
            u.IsMfaEnabled, u.LastLoginAt, u.CreatedAt, roles ?? [], permissions ?? []);
}

/// <summary>
/// Réponse au premier palier d'un login MFA : aucun token d'accès n'est encore émis.
/// Distincte de <see cref="LoginResponse"/> dont les tokens sont non-nullables.
/// </summary>
public sealed record MfaChallengeResponse(
    bool RequiresMfa,
    string MfaToken,
    string[] Methods);
