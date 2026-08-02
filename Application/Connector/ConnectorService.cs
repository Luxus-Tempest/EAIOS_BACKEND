using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

namespace EAIOS.Api.Application.Connector;

public sealed class ConnectorService(
    IConnectorInstanceRepository instanceRepo,
    ISyncJobRepository syncJobRepo) : IConnectorService
{
    public async Task<ConnectorInstance> CreateInstanceAsync(Guid tenantId, Guid definitionId, string name, string? description, Guid? workspaceId, Guid actorId, CancellationToken ct = default)
    {
        var instance = ConnectorInstance.Create(tenantId, definitionId, name, actorId, description: description, workspaceId: workspaceId);
        
        await instanceRepo.AddAsync(instance, ct);
        await instanceRepo.SaveAsync(ct);
        
        return instance;
    }

    public async Task<ConnectorInstance> UpdateInstanceAsync(Guid id, string? name, string? description, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");
        
        instance.UpdateMetadata(name, description);
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);
        
        return instance;
    }

    public async Task DeleteInstanceAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");
        
        instanceRepo.SoftDelete(instance);
        await instanceRepo.SaveAsync(ct);
    }

    public async Task<SyncRunResult> TriggerSyncAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");
        
        if (instance.Status != ConnectorInstanceStatus.Active)
            throw new InvalidOperationException("Le connecteur doit être actif pour lancer une synchronisation.");
            
        instance.RecordSync();
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);
        
        var executionId = Guid.CreateVersion7().ToString("N");
        return new SyncRunResult(executionId, $"/api/v1/connectors/instances/{id}/status/{executionId}");
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");
        
        // Stub
        return new ConnectionTestResult(
            ConnectionSuccessful: true,
            LatencyMs: 45,
            AccessibleItems: 0,
            Permissions: ["read", "write"],
            ErrorMessage: null);
    }

    public async Task<SyncJob> CreateSyncJobAsync(Guid tenantId, Guid instanceId, string name, SyncDirection direction, string? cronExpression, Guid actorId, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct) ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");
        
        var job = SyncJob.Create(tenantId, instanceId, name, direction, actorId, cronExpression);
        
        await syncJobRepo.AddAsync(job, ct);
        await syncJobRepo.SaveAsync(ct);
        
        return job;
    }

    public async Task DeleteSyncJobAsync(Guid instanceId, Guid jobId, CancellationToken ct = default)
    {
        var job = await syncJobRepo.GetByIdAsync(jobId, ct);
        if (job == null || job.ConnectorInstanceId != instanceId)
            throw new KeyNotFoundException("Job de synchronisation introuvable pour cette instance.");
            
        syncJobRepo.SoftDelete(job);
        await syncJobRepo.SaveAsync(ct);
    }
}
