using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Analytics;

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AnalyticsEvent (Append-Only — NO UPDATE, NO DELETE)
// Table: org_{id}.analytics.events (partitioned by occurred_at)
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AnalyticsEvent : TenantEntity
{
    public string EventType { get; private set; } = string.Empty;
    public string? EventCategory { get; private set; }
    public Guid? ActorId { get; private set; }
    public string ActorType { get; private set; } = "User";        // User, Agent, System
    public Guid? ResourceId { get; private set; }
    public string? ResourceType { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public string? SessionId { get; private set; }
    public string PropertiesJson { get; private set; } = "{}";
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public long DurationMs { get; private set; }
    public bool IsSuccessful { get; private set; }
    public DateTime OccurredAt { get; private set; }

    public static AnalyticsEvent Create(Guid organizationId, string eventType, string actorType,
        Guid? actorId = null, Guid? resourceId = null, string? resourceType = null,
        bool isSuccessful = true, long durationMs = 0, string? propertiesJson = null,
        Guid? workspaceId = null, string? sessionId = null)
    {
        var evt = new AnalyticsEvent
        {
            Id = Guid.CreateVersion7(),
            EventType = eventType,
            EventCategory = eventType.Split('.').FirstOrDefault(),
            ActorType = actorType,
            ActorId = actorId,
            ResourceId = resourceId,
            ResourceType = resourceType,
            WorkspaceId = workspaceId,
            SessionId = sessionId,
            PropertiesJson = propertiesJson ?? "{}",
            IsSuccessful = isSuccessful,
            DurationMs = durationMs,
            OccurredAt = DateTime.UtcNow
        };
        evt.SetOrganizationId(organizationId);
        evt.SetCreated(actorId);
        return evt;
    }
}
