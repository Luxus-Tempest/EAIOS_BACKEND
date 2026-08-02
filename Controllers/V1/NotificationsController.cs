using EAIOS.Api.Application.Notification;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Notifications in-app : liste, badge count, marquer comme lu.
/// Route : /api/v1/notifications
/// </summary>
[Route("api/v1/notifications")]
public sealed class NotificationsController(
    INotificationService notifService) : V1ApiController
{
    // ── GET /api/v1/notifications ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? unreadOnly,
        [FromQuery] int   page     = 1,
        [FromQuery] int   pageSize = 20,
        CancellationToken ct       = default)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var result = await notifService.ListAsync(ActorId.Value, unreadOnly, page, pageSize, ct);
        return OkList(result.Items.Select(MapNotif).ToList(), result.TotalCount, page, pageSize);
    }

    // ── GET /api/v1/notifications/count ──────────────────────────────────────
    [HttpGet("count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var count = await notifService.GetUnreadCountAsync(ActorId.Value, ct);
        return Ok200(new { Unread = count });
    }

    // ── POST /api/v1/notifications/{id}/read ─────────────────────────────────
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            await notifService.MarkReadAsync(id, ActorId.Value, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── POST /api/v1/notifications/read-all ──────────────────────────────────
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        await notifService.MarkAllReadAsync(ActorId.Value, ct);
        return NoContent204();
    }

    // ── DELETE /api/v1/notifications/{id} ────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            await notifService.DeleteAsync(id, ActorId.Value, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static object MapNotif(Domain.Notification.Notification n) => new
    {
        n.Id,
        n.Title,
        n.Body,
        n.Type,
        n.Channel,
        n.Priority,
        n.Status,
        n.ActionUrl,
        n.ActionLabel,
        n.ReadAt,
        n.CreatedAt
    };
}
