using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

namespace EAIOS.Api.Infrastructure.Notifications;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface INotificationService
{
    Task SendInAppAsync(
        Guid organizationId,
        Guid recipientId,
        string type,
        string title,
        string? body        = null,
        string? actionUrl   = null,
        string? actionLabel = null,
        NotificationPriority priority = NotificationPriority.Normal,
        CancellationToken ct = default);

    Task SendBulkInAppAsync(
        Guid organizationId,
        IEnumerable<Guid> recipientIds,
        string type,
        string title,
        string? body = null,
        CancellationToken ct = default);
}

// ── In-Memory (dev) implementation ────────────────────────────────────────────

public sealed class InMemoryNotificationService(
    INotificationRepository repository,
    ILogger<InMemoryNotificationService> logger) : INotificationService
{
    public async Task SendInAppAsync(
        Guid organizationId,
        Guid recipientId,
        string type,
        string title,
        string? body        = null,
        string? actionUrl   = null,
        string? actionLabel = null,
        NotificationPriority priority = NotificationPriority.Normal,
        CancellationToken ct = default)
    {
        var notification = Domain.Notification.Notification.Create(
            organizationId: organizationId,
            recipientId: recipientId,
            channel: NotificationChannel.InApp,
            type: type,
            title: title,
            body: body,
            priority: priority,
            actionUrl: actionUrl,
            actionLabel: actionLabel);

        notification.MarkSent();

        await repository.AddAsync(notification, ct);
        await repository.SaveAsync(ct);

        logger.LogInformation(
            "[NOTIFICATION] {Type} sent to user {UserId}: {Title}",
            type, recipientId, title);
    }

    public async Task SendBulkInAppAsync(
        Guid organizationId,
        IEnumerable<Guid> recipientIds,
        string type,
        string title,
        string? body = null,
        CancellationToken ct = default)
    {
        foreach (var recipientId in recipientIds)
        {
            await SendInAppAsync(organizationId, recipientId, type, title, body, ct: ct);
        }
    }
}
