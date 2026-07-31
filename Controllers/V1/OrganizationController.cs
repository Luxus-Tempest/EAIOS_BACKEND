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
    IPermissionService    permService) : V1ApiController
{
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
            u.CreatedAt
        }).ToList(), result.TotalCount, page, pageSize);
    }

    // ── GET /api/v1/organization/users/{userId} ───────────────────────────────
    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> GetUser(Guid userId, CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        return Ok200(new
        {
            user.Id, user.Email, user.FirstName, user.LastName, user.FullName,
            user.DisplayName, user.AvatarUrl, user.JobTitle, user.Department,
            user.Locale, user.TimeZone, user.Status, user.IsEmailVerified,
            user.IsMfaEnabled, user.LastLoginAt, user.CreatedAt
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

        var invitation = Invitation.Create(
            TenantId, req.Email.Trim(), ActorId.Value, role: req.Role, message: req.Message);

        await invitationRepo.AddAsync(invitation, ct);
        await invitationRepo.SaveAsync(ct);

        // TODO (prod) : déclencher IEmailService.SendInvitationEmailAsync(invitation)
        return Ok200(new
        {
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.Status,
            invitation.ExpiresAt,
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

        // TODO (prod) : re-déclencher IEmailService.SendInvitationEmailAsync(invitation)
        return Ok200(new { message = "Invitation renvoyée.", invitation.Id, invitation.Email });
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
