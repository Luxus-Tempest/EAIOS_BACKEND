using EAIOS.Api.Domain.Platform;

namespace EAIOS.Api.Application.Platform;

public sealed record AuditEventDto(
    Guid Id,
    Guid OrganizationId,
    DateTime OccurredAt,
    Guid? ActorId,
    string ActorType,
    string? ActorEmail,
    string? ActorIp,
    string Action,
    string? Module,
    AuditEventResult Result,
    string? FailureReason,
    Guid? ResourceId,
    string? ResourceType,
    string? ResourceName,
    string? CorrelationId);

public sealed record AuditQueryRequest(
    string? Action = null,
    Guid? ActorId = null,
    string? ResourceType = null,
    AuditEventResult? Result = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    int Page = 1,
    int PageSize = 50);

public sealed record FeatureFlagDto(
    Guid Id,
    string Key,
    string? Description,
    FeatureFlagType Type,
    bool DefaultValue,
    string? Module,
    bool IsActive,
    IReadOnlyList<FeatureFlagOverrideDto> Overrides);

public sealed record FeatureFlagOverrideDto(
    Guid Id,
    Guid OrganizationId,
    bool Value,
    string? Reason,
    DateTime? ExpiresAt);

public sealed record UpdateFeatureFlagRequest(
    bool? IsActive,
    bool? DefaultValue,
    string? Description);

public sealed record SetFeatureFlagOverrideRequest(
    Guid OrganizationId,
    bool Value,
    string? Reason = null,
    DateTime? ExpiresAt = null);

public sealed record TenantSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string PlanId,
    int CurrentUsers,
    int MaxUsers,
    long StorageUsedBytes,
    long StorageQuotaBytes,
    DateTime CreatedAt,
    DateTime? TrialEndsAt);
