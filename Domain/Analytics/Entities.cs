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

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: ReportJob — génération asynchrone de rapports/exports
// Table: analytics.report_jobs
// ═══════════════════════════════════════════════════════════════════════════════

public enum ReportJobStatus { Queued, Running, Completed, Failed, Expired }

public sealed class ReportJob : TenantEntity
{
    public string ReportType { get; private set; } = string.Empty;   // dashboard, agents, workflows, search, audit, documents
    public string Format { get; private set; } = "csv";              // csv, json
    public ReportJobStatus Status { get; private set; }
    public DateTime DateFrom { get; private set; }
    public DateTime DateTo { get; private set; }
    public string ParametersJson { get; private set; } = "{}";
    public Guid RequestedBy { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    public string? StorageKey { get; private set; }
    public string? FileName { get; private set; }
    public string? ContentType { get; private set; }
    public long FileSizeBytes { get; private set; }
    public int RowCount { get; private set; }

    /// <summary>Les rapports générés sont purgés après cette date pour ne pas accumuler de fichiers.</summary>
    public DateTime ExpiresAt { get; private set; }

    private ReportJob() { }

    public static ReportJob Create(Guid organizationId, string reportType, string format,
        DateTime dateFrom, DateTime dateTo, Guid requestedBy, string? parametersJson = null,
        int retentionDays = 7)
    {
        var job = new ReportJob
        {
            Id             = Guid.CreateVersion7(),
            ReportType     = reportType.Trim().ToLowerInvariant(),
            Format         = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant(),
            Status         = ReportJobStatus.Queued,
            DateFrom       = dateFrom,
            DateTo         = dateTo,
            ParametersJson = parametersJson ?? "{}",
            RequestedBy    = requestedBy,
            ExpiresAt      = DateTime.UtcNow.AddDays(retentionDays)
        };
        job.SetOrganizationId(organizationId);
        job.SetCreated(requestedBy);
        return job;
    }

    public void MarkRunning()
    {
        Status    = ReportJobStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    public void MarkCompleted(string storageKey, string fileName, string contentType, long sizeBytes, int rowCount)
    {
        Status        = ReportJobStatus.Completed;
        CompletedAt   = DateTime.UtcNow;
        StorageKey    = storageKey;
        FileName      = fileName;
        ContentType   = contentType;
        FileSizeBytes = sizeBytes;
        RowCount      = rowCount;
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        Status        = ReportJobStatus.Failed;
        CompletedAt   = DateTime.UtcNow;
        FailureReason = reason.Length > 1000 ? reason[..1000] : reason;
    }

    public void MarkExpired() => Status = ReportJobStatus.Expired;

    public bool IsDownloadable =>
        Status == ReportJobStatus.Completed && StorageKey is not null && ExpiresAt > DateTime.UtcNow;
}
