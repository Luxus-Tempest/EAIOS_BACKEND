using EAIOS.Api.Domain.Connector;

namespace EAIOS.Api.Application.Connector;

public interface IConnectorService
{
    // ── Instances ─────────────────────────────────────────────────────────────
    Task<ConnectorInstance> CreateInstanceAsync(Guid tenantId, Guid definitionId, string name, string? description, Guid? workspaceId, Guid actorId, CancellationToken ct = default);
    Task<ConnectorInstance> UpdateInstanceAsync(Guid id, string? name, string? description, CancellationToken ct = default);
    Task DeleteInstanceAsync(Guid id, CancellationToken ct = default);
    
    // ── Synchronisation ───────────────────────────────────────────────────────
    Task<SyncRunResult> TriggerSyncAsync(Guid id, CancellationToken ct = default);
    Task<ConnectionTestResult> TestConnectionAsync(Guid id, CancellationToken ct = default);

    // ── Sync Jobs ─────────────────────────────────────────────────────────────
    Task<SyncJob> CreateSyncJobAsync(Guid tenantId, Guid instanceId, string name, SyncDirection direction, string? cronExpression, Guid actorId, CancellationToken ct = default);
    Task DeleteSyncJobAsync(Guid instanceId, Guid jobId, CancellationToken ct = default);
}
