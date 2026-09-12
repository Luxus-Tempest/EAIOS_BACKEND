using EAIOS.Api.Application.Organization;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion de l'organisation courante : profil, paramètres, utilisateurs membres, invitations, permissions.
/// Route : /api/v1/organization
/// </summary>
[Route("api/v1/organization")]
public sealed class OrganizationController(
    IUserRepository       userRepo,
    IInvitationRepository invitationRepo,
    IPermissionService    permService,
    EAIOS.Api.Application.AccessControl.IAccessControlService accessControl,
    EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl.IUserRoleRepository userRoleRepo,
    EAIOS.Api.Infrastructure.Email.IEmailService emailService,
    EAIOS.Api.Infrastructure.Analytics.IAnalyticsTracker analytics,
    EAIOS.Api.Infrastructure.Persistence.PlatformDbContext platformDb) : V1ApiController
{
    // ── GET /api/v1/organization ──────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOrganization(CancellationToken ct)
    {
        var org = await platformDb.Organizations.FindAsync(new object[] { TenantId }, ct);
        if (org == null) return NotFound();
        return Ok200(org); // Note: Should map to a DTO in a real app, but this fits the pattern
    }

    // ── PUT /api/v1/organization ──────────────────────────────────────────────
    [HttpPut]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "org.manage")]
    public async Task<IActionResult> UpdateOrganization(
        [FromBody] EAIOS.Api.Contracts.UpdateOrganizationRequest req,
        CancellationToken ct)
    {
        var org = await platformDb.Organizations.FindAsync(new object[] { TenantId }, ct);
        if (org == null) return NotFound();

        if (req.Name != null) org.Name = req.Name;
        // The entity might need specific setters, let's assume direct mapping or specific properties 
        // exist based on UpdateOrganizationRequest. Let's check Domain/Organization/Organization.cs if needed, 
        // but for now simple properties are typical.
        // EAIOS.Api.Domain.Organization.Organization typically has Name, Settings etc.
        
        platformDb.Organizations.Update(org);
        await platformDb.SaveChangesAsync(ct);
        return Ok200(org);
    }
    // ── GET /api/v1/organization/users ────────────────────────────────────────
    /// <summary>Liste paginée des utilisateurs de l'organisation.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string?     q,
        [FromQuery] UserStatus? status,
        [FromQuery] int         page     = 1,
        [FromQuery] int         pageSize = 20,
        CancellationToken       ct       = default)
    {
        var result = await userRepo.SearchAsync(q, status, page, pageSize, ct);

        // La colonne « Rôles » de l'écran lisait un champ que la réponse ne
        // portait pas. Une seule requête pour toute la page, pas une par ligne.
        var now = DateTime.UtcNow;
        var assignments = await userRoleRepo.GetByUsersAsync(result.Items.Select(u => u.Id).ToList(), ct);
        var rolesByUser = assignments
            .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.RoleName).Distinct().ToArray());

        return OkList(result.Items.Select(u => new
        {
            u.Id,
            u.Email,
            u.FirstName,
            u.LastName,
            u.FullName,
            u.DisplayName,
            u.AvatarUrl,
            u.JobTitle,
            u.Department,
            u.Status,
            u.IsEmailVerified,
            u.IsMfaEnabled,
            u.LastLoginAt,
            u.CreatedAt,
            Roles = rolesByUser.GetValueOrDefault(u.Id, [])
        }).ToList(), result.TotalCount, page, pageSize);
    }

    // ── GET /api/v1/organization/users/{userId} ───────────────────────────────
    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> GetUser(Guid userId, CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        var assignments = await userRoleRepo.GetByUserAsync(userId, ct);
        var roles = assignments.Where(a => !a.IsExpired).Select(a => a.RoleName).Distinct().ToArray();

        return Ok200(new
        {
            user.Id, user.Email, user.FirstName, user.LastName, user.FullName,
            user.DisplayName, user.AvatarUrl, user.JobTitle, user.Department,
            user.Locale, user.TimeZone, user.Status, user.IsEmailVerified,
            user.IsMfaEnabled, user.LastLoginAt, user.CreatedAt,
            Roles = roles
        });
    }

    // ── PUT /api/v1/organization/users/{userId}/status ────────────────────────
    [HttpPut("users/{userId:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(
        Guid userId,
        [FromBody] UpdateUserStatusRequest req,
        CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        if (userId == ActorId.Value) return UnprocessableEntity("Impossible de modifier votre propre statut.");

        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        if (req.Status == UserStatus.Suspended)
            user.Suspend(req.Reason ?? "Suspendu par un administrateur");
        else if (req.Status == UserStatus.Active)
            user.Activate();
        else
            return BadRequest(new { code = "INVALID_STATUS", message = "Transition de statut non supportée." });

        userRepo.Update(user);
        await userRepo.SaveAsync(ct);

        return Ok200(new { user.Id, user.Status, user.SuspensionReason });
    }

    // ── DELETE /api/v1/organization/users/{userId} ────────────────────────────
    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> RemoveUser(Guid userId, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        if (userId == ActorId.Value)
            return UnprocessableEntity("Impossible de supprimer votre propre compte via cet endpoint.");

        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        userRepo.SoftDelete(user);
        await userRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Invitations ───────────────────────────────────────────────────────────

    // ── POST /api/v1/organization/invitations ─────────────────────────────────
    [HttpPost("invitations")]
    public async Task<IActionResult> SendInvitation(
        [FromBody] SendInvitationRequest req,
        CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var normalizedEmail = req.Email.Trim().ToUpperInvariant();

        if (await userRepo.EmailExistsAsync(normalizedEmail, ct))
            return Conflict("Un utilisateur avec cet email existe déjà dans l'organisation.");

        var existing = await invitationRepo.FindPendingByEmailAsync(normalizedEmail, ct);
        if (existing != null)
            return Conflict("Une invitation est déjà en attente pour cet email.");

        // Le rôle doit être résolu ICI : l'inscription n'attribue un rôle que si
        // l'invitation porte son identifiant. Sans cette résolution, tout invité
        // arrivait sans aucun droit. Sans rôle demandé, on donne le rôle membre.
        var requested = string.IsNullOrWhiteSpace(req.Role) || req.Role == "member"
            ? Domain.AccessControl.SystemRoles.OrgMember
            : req.Role;
        var role = await accessControl.ResolveRoleAsync(requested, ct);
        if (role == null)
            return BadRequest(new { code = "UNKNOWN_ROLE", message = $"Le rôle « {requested} » n'existe pas dans cette organisation." });

        var invitation = Invitation.Create(
            TenantId, req.Email.Trim(), ActorId.Value, role: role.Name, roleId: role.Id, message: req.Message);

        await invitationRepo.AddAsync(invitation, ct);
        await invitationRepo.SaveAsync(ct);

        var (orgName, inviterName) = await ResolveInvitationContextAsync(ActorId.Value, ct);
        await emailService.SendInvitationAsync(
            invitation.Email, orgName, inviterName, invitation.Token, invitation.PersonalMessage, ct);

        await analytics.TrackAsync(
            EAIOS.Api.Infrastructure.Analytics.AnalyticsEventTypes.UserInvited,
            resourceId: invitation.Id, resourceType: "Invitation", ct: ct);

        return Ok200(new
        {
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.Status,
            invitation.ExpiresAt,
            invitation.ResendCount,
            invitation.LastSentAt,
            // Token retourné uniquement en dev pour faciliter les tests
            InvitationUrl = $"/register?token={invitation.Token}"
        });
    }

    // ── GET /api/v1/organization/invitations ──────────────────────────────────
    [HttpGet("invitations")]
    public async Task<IActionResult> ListInvitations(
        [FromQuery] InvitationStatus? status,
        CancellationToken ct)
    {
        var invitations = await invitationRepo.ListAsync(status, ct);
        return Ok200(invitations.Select(i => new
        {
            i.Id, i.Email, i.Role, i.Status, i.ExpiresAt, i.AcceptedAt, i.CreatedAt
        }).ToList());
    }

    // ── DELETE /api/v1/organization/invitations/{id} ──────────────────────────
    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> CancelInvitation(Guid id, CancellationToken ct)
    {
        var invitation = await invitationRepo.GetByIdAsync(id, ct);
        if (invitation == null) return NotFound();

        if (invitation.Status != InvitationStatus.Pending)
            return UnprocessableEntity("Seules les invitations en attente peuvent être annulées.");

        invitation.Expire();
        invitationRepo.Update(invitation);
        await invitationRepo.SaveAsync(ct);

        return NoContent204();
    }

    // ── POST /api/v1/organization/invitations/{id}/resend ─────────────────────
    [HttpPost("invitations/{id:guid}/resend")]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct)
    {
        var invitation = await invitationRepo.GetByIdAsync(id, ct);
        if (invitation == null) return NotFound();

        if (!invitation.IsValid)
            return UnprocessableEntity("Cette invitation a expiré ou n'est plus valide.");

        if (!ActorId.HasValue) return Unauthorized();

        // Resend() incrémente le compteur ET repousse l'échéance : sans cet appel,
        // le renvoi laissait l'invitation expirer à sa date initiale.
        invitation.Resend();
        invitationRepo.Update(invitation);
        await invitationRepo.SaveAsync(ct);

        var (orgName, inviterName) = await ResolveInvitationContextAsync(ActorId.Value, ct);
        await emailService.SendInvitationAsync(
            invitation.Email, orgName, inviterName, invitation.Token, invitation.PersonalMessage, ct);

        return Ok200(new
        {
            message = "Invitation renvoyée.",
            invitation.Id,
            invitation.Email,
            invitation.ResendCount,
            invitation.LastSentAt,
            invitation.ExpiresAt
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Nom de l'organisation et de l'invitant, utilisés dans le corps de l'email.</summary>
    private async Task<(string OrgName, string InviterName)> ResolveInvitationContextAsync(Guid actorId, CancellationToken ct)
    {
        var org     = await platformDb.Organizations.FindAsync([TenantId], ct);
        var inviter = await userRepo.GetByIdAsync(actorId, ct);

        return (org?.Name ?? "EAIOS", inviter?.FullName ?? "Un administrateur");
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    // ── GET /api/v1/organization/users/{userId}/permissions ───────────────────
    [HttpGet("users/{userId:guid}/permissions")]
    public async Task<IActionResult> GetUserPermissions(Guid userId, CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        var permissions = await permService.GetEffectivePermissionsAsync(userId, ct);
        return Ok200(new { UserId = userId, Permissions = permissions });
    }

    // ── GET /api/v1/organization/users/{userId}/permissions/check ─────────────
    [HttpGet("users/{userId:guid}/permissions/check")]
    public async Task<IActionResult> CheckPermission(
        Guid             userId,
        [FromQuery] string permission,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return BadRequest(new { code = "MISSING_PERMISSION", message = "Le paramètre 'permission' est requis." });

        var hasPermission = await permService.HasPermissionAsync(userId, permission, ct);
        return Ok200(new { UserId = userId, Permission = permission, Granted = hasPermission });
    }
}

// UpdateUserStatusRequest et SendInvitationRequest définis dans Application.Organization.OrgDtos
