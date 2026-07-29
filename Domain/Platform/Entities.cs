using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Platform;

public enum AuditEventResult { Success, Failure, PartialSuccess }
public enum FeatureFlagType { Boolean, Percentage, Variant }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AuditEvent
// APPEND-ONLY ABSOLUTE — No UPDATE, No DELETE, Never.
// Table: audit.events
// Retention: 10 years (legally mandated in some sectors)
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AuditEvent
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    // ── Actor ─────────────────────────────────────────────────────────────────
    public Guid? ActorId { get; init; }
    public string ActorType { get; init; } = "User";   // User, Agent, System, ApiKey
    public string? ActorEmail { get; init; }
    public string? ActorIp { get; init; }
    public string? ActorUserAgent { get; init; }

    // ── Action ────────────────────────────────────────────────────────────────
    public string Action { get; init; } = string.Empty;
    public string? Module { get; init; }
    public AuditEventResult Result { get; init; }
    public string? FailureReason { get; init; }

    // ── Resource ──────────────────────────────────────────────────────────────
    public Guid? ResourceId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceName { get; init; }

    // ── Diff ──────────────────────────────────────────────────────────────────
    public string? OldValuesJson { get; init; }
    public string? NewValuesJson { get; init; }

    // ── Correlation ───────────────────────────────────────────────────────────
    public string? CorrelationId { get; init; }
    public string? RequestId { get; init; }
    public string? SessionId { get; init; }
    public string? ApiKeyId { get; init; }
    public string? AdditionalDataJson { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: FeatureFlag (Platform-level)
// Table: platform.feature_flags
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class FeatureFlag : Entity<Guid>
{
    public string Key { get; set; } = string.Empty;   // e.g. "knowledge.graph.enabled"
    public string? Description { get; set; }
    public FeatureFlagType Type { get; set; }
    public bool DefaultValue { get; set; }
    public string? Module { get; set; }
    public bool IsActive { get; set; }

    public List<FeatureFlagOverride> Overrides { get; set; } = [];
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: FeatureFlagOverride
// Per-tenant override for feature flags
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class FeatureFlagOverride : Entity<Guid>
{
    public Guid FeatureFlagId { get; set; }
    public Guid OrganizationId { get; set; }
    public bool Value { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    public bool IsActive => !IsExpired;
}
