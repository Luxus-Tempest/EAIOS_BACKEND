using EAIOS.Api.Domain.Connector;

namespace EAIOS.Api.Application.Connector;

public interface IConnectorService
{
    // ── Instances ─────────────────────────────────────────────────────────────
    Task<ConnectorInstance> CreateInstanceAsync(Guid tenantId, Guid definitionId, string name, string? description, Guid? workspaceId, Guid actorId, CancellationToken ct = default);

    /// <summary>Surcharge complete : la configuration est stockee en clair, les identifiants chiffres.</summary>
    Task<ConnectorInstance> CreateInstanceAsync(Guid tenantId, Guid definitionId, string name, string? description,
        Guid? workspaceId, Guid actorId, Dictionary<string, string>? configuration,
        Dictionary<string, string>? credentials, CancellationToken ct = default);

    Task<ConnectorInstance> UpdateInstanceAsync(Guid id, string? name, string? description, CancellationToken ct = default);

    /// <summary>Surcharge complete. Des identifiants absents laissent ceux deja en place intacts.</summary>
    Task<ConnectorInstance> UpdateInstanceAsync(Guid id, string? name, string? description,
        Dictionary<string, string>? configuration, Dictionary<string, string>? credentials,
        CancellationToken ct = default);
    Task DeleteInstanceAsync(Guid id, CancellationToken ct = default);
    
    // ── Synchronisation ───────────────────────────────────────────────────────
    Task<SyncRunResult> TriggerSyncAsync(Guid id, CancellationToken ct = default);
    Task<ConnectionTestResult> TestConnectionAsync(Guid id, CancellationToken ct = default);

    // ── Sync Jobs ─────────────────────────────────────────────────────────────
    Task<SyncJob> CreateSyncJobAsync(Guid tenantId, Guid instanceId, string name, SyncDirection direction, string? cronExpression, Guid actorId, CancellationToken ct = default);
    Task DeleteSyncJobAsync(Guid instanceId, Guid jobId, CancellationToken ct = default);
}
