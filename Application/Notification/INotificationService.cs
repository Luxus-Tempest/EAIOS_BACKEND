using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Application.Common.Models;

namespace EAIOS.Api.Application.Notification;

public interface INotificationService
{
    Task<PagedResult<Domain.Notification.Notification>> ListAsync(Guid recipientId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid recipientId, CancellationToken ct = default);
    Task MarkReadAsync(Guid id, Guid recipientId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid recipientId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid recipientId, CancellationToken ct = default);
}

public interface INotificationTemplateService
{
    Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken ct = default);
    Task<NotificationTemplate> GetTemplateAsync(Guid id, CancellationToken ct = default);
    Task<NotificationTemplate> CreateTemplateAsync(Guid tenantId, string eventType, NotificationChannel channel, string language, string subjectTemplate, string bodyTemplate, Guid actorId, bool isSystem, CancellationToken ct = default);
    Task<NotificationTemplate> UpdateTemplateAsync(Guid id, string? subjectTemplate, string? bodyTemplate, bool? isActive, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);
}
