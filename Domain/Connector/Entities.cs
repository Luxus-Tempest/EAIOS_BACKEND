using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Connector;

public enum ConnectorCategory { Ecm, Crm, Erp, Communication, Storage, Calendar, Hr, Custom }
public enum ConnectorAuthType { ApiKey, OAuth2, BasicAuth, Jwt, SamlAssertion, ServiceAccount }
public enum ConnectorInstanceStatus { Configuring, Active, Paused, Error, Disconnected }
public enum SyncHealth { Unknown, Healthy, Degraded, Critical }
public enum SyncDirection { Import, Export, Bidirectional }
public enum SyncJobStatus { Active, Paused, Disabled }
public enum SyncJobLastRunResult { Success, PartialSuccess, Failed }
public enum ConflictResolutionStrategy { SourceWins, DestinationWins, Manual, Timestamp }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: ConnectorDefinition (Platform-level catalog — NOT tenant scoped)
// Table: platform.connector_definitions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class ConnectorDefinition : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public ConnectorCategory Category { get; set; }
    public ConnectorAuthType AuthType { get; set; }
    public string SchemaJson { get; set; } = "{}";
    public string[] SupportedCapabilities { get; set; } = [];
    public string Version { get; set; } = "1.0.0";
    public bool IsActive { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: ConnectorInstance (Tenant-scoped)
// Table: org_{id}.connector.instances
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class ConnectorInstance : TenantEntity
{
    public Guid DefinitionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public ConnectorInstanceStatus Status { get; private set; }
    public string ConfigurationJson { get; private set; } = "{}";
    public string? CredentialsEncrypted { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public SyncHealth Health { get; private set; }
    public string? LastHealthCheckMessage { get; private set; }

    public IReadOnlyList<SyncJob> SyncJobs { get; private set; } = [];

    public static ConnectorInstance Create(Guid organizationId, Guid definitionId, string name,
        Guid createdBy, string? description = null, Guid? workspaceId = null,
        string? configJson = null, string? credentialsEncrypted = null)
    {
        var ci = new ConnectorInstance
        {
            Id = Guid.CreateVersion7(),
            DefinitionId = definitionId,
            Name = name.Trim(),
            Description = description,
            WorkspaceId = workspaceId,
            Status = ConnectorInstanceStatus.Configuring,
            Health = SyncHealth.Unknown,
            ConfigurationJson = configJson ?? "{}",
            CredentialsEncrypted = credentialsEncrypted
        };
        ci.SetOrganizationId(organizationId);
        ci.SetCreated(createdBy);
        return ci;
    }

    public void UpdateMetadata(string name, string? description)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (description is not null) Description = description;
    }

    public void Activate() => Status = ConnectorInstanceStatus.Active;
    public void Pause() => Status = ConnectorInstanceStatus.Paused;
    public void SetError(string message) { Status = ConnectorInstanceStatus.Error; LastHealthCheckMessage = message; Health = SyncHealth.Critical; }
    public void UpdateHealth(SyncHealth health, string? message) { Health = health; LastHealthCheckMessage = message; }
    public void RecordSync() => LastSyncAt = DateTime.UtcNow;
    public void UpdateCredentials(string encrypted) => CredentialsEncrypted = encrypted;
    public void UpdateConfig(string configJson) => ConfigurationJson = configJson;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: SyncJob
// Table: org_{id}.connector.sync_jobs
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class SyncJob : TenantEntity
{
    public Guid ConnectorInstanceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public SyncDirection Direction { get; private set; }
    public SyncJobStatus Status { get; private set; }
    public string? CronExpression { get; private set; }
    public DateTime? NextRunAt { get; private set; }
    public DateTime? LastRunAt { get; private set; }
    public SyncJobLastRunResult? LastRunResult { get; private set; }
    public int TotalSynced { get; private set; }
    public string FilterConfigJson { get; private set; } = "{}";
    public string FieldMappingJson { get; private set; } = "{}";
    public ConflictResolutionStrategy ConflictStrategy { get; private set; }

    public static SyncJob Create(Guid organizationId, Guid connectorInstanceId, string name,
        SyncDirection direction, Guid createdBy, string? cronExpression = null)
    {
        var job = new SyncJob
        {
            Id = Guid.CreateVersion7(),
            ConnectorInstanceId = connectorInstanceId,
            Name = name.Trim(),
            Direction = direction,
            Status = SyncJobStatus.Active,
            CronExpression = cronExpression,
            ConflictStrategy = ConflictResolutionStrategy.SourceWins
        };
        job.SetOrganizationId(organizationId);
        job.SetCreated(createdBy);
        return job;
    }

    public void Pause() => Status = SyncJobStatus.Paused;
    public void Activate() => Status = SyncJobStatus.Active;

    public void RecordRun(SyncJobLastRunResult result, int syncedCount)
    {
        LastRunAt = DateTime.UtcNow;
        LastRunResult = result;
        TotalSynced += syncedCount;
    }

    public void ScheduleNextRun(DateTime nextRun) => NextRunAt = nextRun;
}
