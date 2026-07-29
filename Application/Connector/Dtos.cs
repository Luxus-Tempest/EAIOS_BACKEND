using EAIOS.Api.Domain.Connector;

namespace EAIOS.Api.Application.Connector;

public sealed record ConnectorDefinitionDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    ConnectorCategory Category,
    ConnectorAuthType AuthType,
    string[] SupportedCapabilities,
    string Version,
    bool IsActive);

public sealed record ConnectorInstanceDto(
    Guid Id,
    Guid DefinitionId,
    string DefinitionName,
    string? DefinitionLogoUrl,
    string Name,
    string? Description,
    ConnectorInstanceStatus Status,
    SyncHealth Health,
    string? LastHealthCheckMessage,
    DateTime? LastSyncAt,
    DateTime CreatedAt);

public sealed record CreateConnectorRequest(
    Guid DefinitionId,
    string Name,
    string? Description = null,
    Dictionary<string, string>? Configuration = null,
    Dictionary<string, string>? Credentials = null,
    Guid? WorkspaceId = null);

public sealed record UpdateConnectorRequest(
    string? Name,
    string? Description,
    Dictionary<string, string>? Configuration,
    Dictionary<string, string>? Credentials);

public sealed record ConnectionTestResult(
    bool ConnectionSuccessful,
    int LatencyMs,
    long AccessibleItems,
    string[] Permissions,
    string? ErrorMessage = null);

public sealed record SyncJobDto(
    Guid Id,
    string Name,
    SyncDirection Direction,
    SyncJobStatus Status,
    string? CronExpression,
    DateTime? NextRunAt,
    DateTime? LastRunAt,
    SyncJobLastRunResult? LastRunResult,
    int TotalSynced,
    DateTime CreatedAt);

public sealed record CreateSyncJobRequest(
    string Name,
    SyncDirection Direction = SyncDirection.Import,
    string? CronExpression = null,
    Dictionary<string, string>? FilterConfig = null,
    Dictionary<string, string>? FieldMapping = null,
    ConflictResolutionStrategy ConflictStrategy = ConflictResolutionStrategy.SourceWins);

public sealed record SyncRunResult(
    string ExecutionId,
    string StatusUrl);

public sealed record SyncExecutionDto(
    string ExecutionId,
    Guid SyncJobId,
    string Status,
    int Discovered,
    int Imported,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors,
    DateTime StartedAt,
    DateTime? CompletedAt);
