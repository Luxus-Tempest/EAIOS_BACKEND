using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Notification;

public enum NotificationChannel { InApp, Email, Sms, PushWeb, PushMobile, Webhook }
public enum NotificationPriority { Low, Normal, High, Critical }
public enum NotificationStatus { Pending, Queued, Sent, Delivered, Failed, Cancelled }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Notification
// Table: org_{id}.notification.notifications
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Notification : TenantEntity
{
    public Guid RecipientId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Type { get; private set; } = string.Empty;  // e.g. "workflow.task_assigned"
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public string? ActionUrl { get; private set; }
    public string? ActionLabel { get; private set; }
    public string? DataJson { get; private set; }
    public NotificationPriority Priority { get; private set; }
    public NotificationStatus Status { get; private set; }
    public DateTime? ScheduledAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? ReadAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ExternalDeliveryId { get; private set; }
    public Guid? GroupId { get; private set; }
    public bool IsDigested { get; private set; }

    public bool IsRead => ReadAt.HasValue;

    public static Notification Create(Guid organizationId, Guid recipientId, NotificationChannel channel,
        string type, string title, string? body = null, NotificationPriority priority = NotificationPriority.Normal,
        string? actionUrl = null, string? actionLabel = null, string? dataJson = null)
    {
        var n = new Notification
        {
            Id = Guid.CreateVersion7(),
            RecipientId = recipientId,
            Channel = channel,
            Type = type,
            Title = title,
            Body = body,
            ActionUrl = actionUrl,
            ActionLabel = actionLabel,
            DataJson = dataJson,
            Priority = priority,
            Status = NotificationStatus.Pending
        };
        n.SetOrganizationId(organizationId);
        n.SetCreated(null);
        return n;
    }

    public void MarkSent(string? externalId = null) { Status = NotificationStatus.Sent; SentAt = DateTime.UtcNow; ExternalDeliveryId = externalId; }
    public void MarkDelivered() => Status = NotificationStatus.Delivered;
    public void MarkRead() => ReadAt = DateTime.UtcNow;
    public void MarkFailed(string error) { Status = NotificationStatus.Failed; ErrorMessage = error; RetryCount++; }
    public void Cancel() => Status = NotificationStatus.Cancelled;
    public void Queue() => Status = NotificationStatus.Queued;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: NotificationTemplate
// Table: org_{id}.notification.templates
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class NotificationTemplate : TenantEntity
{
    public string EventType { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public string Language { get; private set; } = "fr";
    public string SubjectTemplate { get; private set; } = string.Empty;
    public string BodyTemplate { get; private set; } = string.Empty;
    public string? HtmlBodyTemplate { get; private set; }
    public string[] Variables { get; private set; } = [];
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }

    public static NotificationTemplate Create(Guid organizationId, string eventType, NotificationChannel channel,
        string language, string subjectTemplate, string bodyTemplate, Guid createdBy, bool isSystem = false)
    {
        var t = new NotificationTemplate
        {
            Id = Guid.CreateVersion7(),
            EventType = eventType,
            Channel = channel,
            Language = language,
            SubjectTemplate = subjectTemplate,
            BodyTemplate = bodyTemplate,
            IsSystem = isSystem,
            IsActive = true
        };
        t.SetOrganizationId(organizationId);
        t.SetCreated(createdBy);
        return t;
    }
}
