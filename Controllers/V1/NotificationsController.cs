using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Notifications : liste, badge count, mark read.
/// </summary>
[Route("api/v1/notifications")]
public sealed class NotificationsController(INotificationRepository notifRepo) : V1ApiController
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? unreadOnly,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var result = await notifRepo.GetByRecipientAsync(ActorId.Value, unreadOnly, page, pageSize, ct);
        return OkList(result.Items.Select(MapNotif).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var count = await notifRepo.GetUnreadCountAsync(ActorId.Value, ct);
        return Ok200(new { unread = count });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var notif = await notifRepo.GetByIdAsync(id, ct);
        if (notif == null || notif.RecipientId != ActorId.Value) return NotFound();
        notif.MarkRead();
        notifRepo.Update(notif);
        await notifRepo.SaveAsync(ct);
        return NoContent204();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        await notifRepo.MarkAllReadAsync(ActorId.Value, ct);
        await notifRepo.SaveAsync(ct);
        return NoContent204();
    }

    private static object MapNotif(Domain.Notification.Notification n) => new
    {
        n.Id, n.Title, n.Body, n.Type, n.Channel, n.Priority, n.Status,
        n.ActionUrl, n.ActionLabel, n.ReadAt, n.CreatedAt
    };
}

/// <summary>
/// Analytics : summary dashboard, usage.
/// </summary>
[Route("api/v1/analytics")]
public sealed class AnalyticsController(IAnalyticsEventRepository analyticsRepo) : V1ApiController
{
    [HttpGet("summary")]
    public IActionResult Summary(CancellationToken ct)
    {
        // En production : requêtes agrégées par période
        return Ok200(new
        {
            TotalDocuments = 0,
            TotalAgentExecutions = 0,
            TotalSearches = 0,
            TotalWorkflowInstances = 0,
            ActiveUsers = 0,
            Message = "Analytics agrégées disponibles en production via PostgreSQL."
        });
    }
}

/// <summary>
/// Organisation : détails, paramètres, feature flags, invitations.
/// </summary>
[Route("api/v1/organization")]
public sealed class OrganizationController(
    Infrastructure.Persistence.Repositories.Identity.IInvitationRepository invitationRepo,
    Infrastructure.Persistence.Repositories.Identity.IUserRepository userRepo,
    Infrastructure.Security.IPermissionService permService) : V1ApiController
{
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? q,
        [FromQuery] Domain.Identity.UserStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await userRepo.SearchAsync(q, status, page, pageSize, ct);
        return OkList(result.Items.Select(u => new
        {
            u.Id, u.Email, u.FirstName, u.LastName, u.DisplayName,
            u.Status, u.IsEmailVerified, u.IsMfaEnabled, u.LastLoginAt, u.CreatedAt
        }).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var normalizedEmail = req.Email.Trim().ToUpperInvariant();
        if (await userRepo.EmailExistsAsync(normalizedEmail, ct))
            return Conflict("Un utilisateur avec cet email existe déjà.");

        var existing = await invitationRepo.FindPendingByEmailAsync(normalizedEmail, ct);
        if (existing != null)
            return Conflict("Une invitation est déjà en attente pour cet email.");

        var invitation = Domain.Identity.Invitation.Create(TenantId, req.Email, req.Role, ActorId.Value, 7);
        await invitationRepo.AddAsync(invitation, ct);
        await invitationRepo.SaveAsync(ct);

        // En production : envoyer l'email via IEmailService
        return Ok200(new
        {
            invitation.Id,
            invitation.Email,
            invitation.Token, // À supprimer en prod (envoi email uniquement)
            invitation.ExpiresAt,
            invitation.Status
        });
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> ListInvitations([FromQuery] Domain.Identity.InvitationStatus? status, CancellationToken ct)
    {
        var invitations = await invitationRepo.ListAsync(status, ct);
        return Ok200(invitations.Select(i => new
        {
            i.Id, i.Email, i.Status, i.Role, i.ExpiresAt, i.AcceptedAt, i.CreatedAt
        }).ToList());
    }

    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> CancelInvitation(Guid id, CancellationToken ct)
    {
        var invitation = await invitationRepo.GetByIdAsync(id, ct);
        if (invitation == null) return NotFound();
        invitation.Expire();
        invitationRepo.Update(invitation);
        await invitationRepo.SaveAsync(ct);
        return NoContent204();
    }

    [HttpGet("users/{userId:guid}/permissions")]
    public async Task<IActionResult> GetUserPermissions(Guid userId, CancellationToken ct)
    {
        var permissions = await permService.GetEffectivePermissionsAsync(userId, ct);
        return Ok200(new { UserId = userId, Permissions = permissions });
    }
}

public sealed record SendInvitationRequest(string Email, string Role, string? Message);
