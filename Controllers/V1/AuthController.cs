using EAIOS.Api.Application.Identity;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Authentication : login, refresh, logout, register, profil, MFA, sessions, API keys.
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController(
    IUserRepository        userRepo,
    ISessionRepository     sessionRepo,
    IInvitationRepository  invitationRepo,
    IMfaCredentialRepository mfaRepo,
    IApiKeyRepository      apiKeyRepo,
    ITokenService          tokenService,
    IPasswordService       passwordService,
    ITotpService           totpService,
    IApiKeyService         apiKeyService) : V1ApiController
{
    // ── POST /api/v1/auth/login ───────────────────────────────────────────────
    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(req.Email.Trim().ToUpperInvariant(), ct);

        if (user == null || !passwordService.VerifyPassword(req.Password, user.PasswordHash ?? ""))
        {
            await Task.Delay(300, ct); // Constant-time delay to prevent timing attacks
            return Unauthorized(new { code = "INVALID_CREDENTIALS", message = "Email ou mot de passe incorrect." });
        }

        if (user.Status == UserStatus.Suspended)
            return StatusCode(403, new { code = "ACCOUNT_SUSPENDED", message = user.SuspensionReason });

        // MFA step-up
        if (user.IsMfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(req.MfaCode))
            {
                return Ok(new LoginResponse(
                    AccessToken: null, RefreshToken: null, ExpiresIn: 0,
                    User: MapUser(user), RequiresMfa: true, MfaToken: Guid.CreateVersion7().ToString("N")));
            }

            var mfaCred = await mfaRepo.FindByUserAndMethodAsync(user.Id, MfaMethod.Totp, ct);
            if (mfaCred == null || !totpService.VerifyCode(mfaCred.SecretEncrypted ?? "", req.MfaCode))
                return Unauthorized(new { code = "INVALID_MFA_CODE", message = "Code MFA invalide." });
        }

        // Déterminer les rôles de l'utilisateur
        var roles = user.Email.Equals("admin@eaios.io", StringComparison.OrdinalIgnoreCase)
            ? new[] { "platform.admin", "Admin" }
            : new[] { "User" };

        // Créer la session
        var tokenPair = tokenService.Issue(user.Id, user.OrganizationId, Guid.CreateVersion7(), roles, []);
        var session   = Session.Create(
            user.OrganizationId, user.Id,
            tokenPair.RefreshTokenHash,
            7,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString());

        await sessionRepo.AddAsync(session, ct);
        user.RecordLogin(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        await userRepo.SaveAsync(ct);
        await sessionRepo.SaveAsync(ct);

        return Ok200(new LoginResponse(
            AccessToken:  tokenPair.AccessToken,
            RefreshToken: tokenPair.RefreshToken,
            ExpiresIn:    (int)(tokenPair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            User:         MapUser(user)));
    }

    // ── POST /api/v1/auth/refresh ─────────────────────────────────────────────
    [HttpPost("refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        var hash    = tokenService.HashRefreshToken(req.RefreshToken);
        var session = await sessionRepo.FindByRefreshTokenHashAsync(hash, ct);

        if (session == null || session.ExpiresAt < DateTime.UtcNow)
            return Unauthorized(new { code = "INVALID_REFRESH_TOKEN" });

        var user = await userRepo.GetByIdAsync(session.UserId, ct);
        if (user == null || user.Status != UserStatus.Active)
            return Unauthorized(new { code = "USER_NOT_FOUND_OR_INACTIVE" });

        var newPair = tokenService.Issue(user.Id, user.OrganizationId, session.Id, [], []);
        session.RotateRefreshToken(newPair.RefreshTokenHash);
        await sessionRepo.SaveAsync(ct);

        return Ok200(new RefreshResponse(
            newPair.AccessToken,
            newPair.RefreshToken,
            (int)(newPair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
    }

    // ── POST /api/v1/auth/logout ──────────────────────────────────────────────
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
    {
        var hash    = tokenService.HashRefreshToken(req.RefreshToken);
        var session = await sessionRepo.FindByRefreshTokenHashAsync(hash, ct);
        if (session != null)
        {
            session.Revoke("user_logout");
            await sessionRepo.SaveAsync(ct);
        }
        return NoContent204();
    }

    // ── POST /api/v1/auth/register ────────────────────────────────────────────
    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var invitation = await invitationRepo.FindByTokenAsync(req.InvitationToken, ct);
        if (invitation == null || !invitation.IsValid)
            return BadRequest(new { code = "INVALID_INVITATION", message = "Invitation invalide ou expirée." });

        if (!passwordService.IsStrongPassword(req.Password))
            return BadRequest(new { code = "WEAK_PASSWORD", message = "Le mot de passe ne respecte pas les critères de sécurité." });

        var normalizedEmail = req.Email.Trim().ToUpperInvariant();
        if (await userRepo.EmailExistsAsync(normalizedEmail, ct))
            return Conflict("Un compte avec cet email existe déjà.");

        var user = Domain.Identity.User.Create(invitation.OrganizationId, req.Email.Trim(), req.FirstName, req.LastName);
        user.SetPasswordHash(passwordService.HashPassword(req.Password));
        user.VerifyEmail(user.EmailVerificationToken ?? "");

        await userRepo.AddAsync(user, ct);
        invitation.Accept(user.Id);
        await userRepo.SaveAsync(ct);

        return Ok200(MapUser(user));
    }

    // ── MFA : Setup ───────────────────────────────────────────────────────────
    [HttpPost("mfa/setup")]
    [Authorize]
    public async Task<IActionResult> MfaSetup(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound();

        var secret      = totpService.GenerateSecret();
        var qrUri       = totpService.BuildQrCodeUri(user.Email, secret);
        var backupCodes = totpService.GenerateBackupCodes();

        return Ok200(new MfaSetupDto(secret, qrUri, backupCodes));
    }

    // ── MFA : Enable ──────────────────────────────────────────────────────────
    [HttpPost("mfa/enable")]
    [Authorize]
    public async Task<IActionResult> MfaEnable([FromBody] EnableTotpRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound();

        if (!totpService.VerifyCode(req.Secret, req.Code))
            return BadRequest(new { code = "INVALID_TOTP_CODE", message = "Code de vérification invalide." });

        var cred = MfaCredential.Create(user.OrganizationId, user.Id, MfaMethod.Totp, req.Secret);
        var backupHashes = req.BackupCodes.Select(c => totpService.HashBackupCode(c)).ToArray();
        cred.Activate(System.Text.Json.JsonSerializer.Serialize(backupHashes));

        await mfaRepo.AddAsync(cred, ct);
        user.EnableMfa(MfaMethod.Totp);
        await userRepo.SaveAsync(ct);
        await mfaRepo.SaveAsync(ct);

        return Ok(new { message = "MFA TOTP activé avec succès." });
    }

    // ── MFA : Disable ─────────────────────────────────────────────────────────
    [HttpPost("mfa/disable")]
    [Authorize]
    public async Task<IActionResult> MfaDisable([FromBody] DisableMfaRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound();

        if (!passwordService.VerifyPassword(req.Password, user.PasswordHash ?? ""))
            return Unauthorized(new { code = "INVALID_PASSWORD" });

        user.DisableMfa(MfaMethod.Totp);
        var creds = await mfaRepo.GetActiveByUserAsync(user.Id, ct);
        foreach (var c in creds) mfaRepo.SoftDelete(c);

        await userRepo.SaveAsync(ct);
        return Ok(new { message = "MFA désactivé." });
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static UserDto MapUser(User u) =>
        new(u.Id, u.OrganizationId, u.Email, u.FirstName, u.LastName, u.FullName, u.DisplayName,
            u.AvatarUrl, u.JobTitle, u.Department, u.Locale, u.TimeZone, u.Status, u.IsEmailVerified,
            u.IsMfaEnabled, u.LastLoginAt, u.CreatedAt, [], []);
}
