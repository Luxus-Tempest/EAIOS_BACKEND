using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Application.Connector;
using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Connecteurs externes : instances, synchronisation, statut de santé.
/// Route : /api/v1/connectors
/// </summary>
[Route("api/v1/connectors")]
public sealed class ConnectorsController(
    IConnectorInstanceRepository instanceRepo,
    ISyncJobRepository           syncJobRepo) : V1ApiController
{
    // ── GET /api/v1/connectors/instances ─────────────────────────────────────
    [HttpGet("instances")]
    public async Task<IActionResult> ListInstances(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct     = default)
    {
        var instances = await instanceRepo.GetAllAsync(ct);
        var dtos = instances.Select(MapInstance).ToList();
        return OkList(dtos, dtos.Count, page, pageSize);
    }

    // ── GET /api/v1/connectors/instances/{id} ─────────────────────────────────
    [HttpGet("instances/{id:guid}", Name = "GetConnectorInstance")]
    public async Task<IActionResult> GetInstance(Guid id, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct);
        return instance == null ? NotFound() : Ok200(MapInstance(instance));
    }

    // ── POST /api/v1/connectors/instances ────────────────────────────────────
    [HttpPost("instances")]
    public async Task<IActionResult> CreateInstance(
        [FromBody] CreateConnectorRequest req,
        CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var instance = ConnectorInstance.Create(
            TenantId, req.DefinitionId, req.Name, ActorId.Value,
            description: req.Description,
            workspaceId: req.WorkspaceId);

        await instanceRepo.AddAsync(instance, ct);
        await instanceRepo.SaveAsync(ct);

        return Created201("GetConnectorInstance", new { id = instance.Id }, MapInstance(instance));
    }

    // ── PUT /api/v1/connectors/instances/{id} ────────────────────────────────
    [HttpPut("instances/{id:guid}")]
    public async Task<IActionResult> UpdateInstance(
        Guid id,
        [FromBody] UpdateConnectorRequest req,
        CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct);
        if (instance == null) return NotFound();

        instance.UpdateMetadata(req.Name, req.Description);
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);

        return Ok200(MapInstance(instance));
    }

    // ── DELETE /api/v1/connectors/instances/{id} ──────────────────────────────
    [HttpDelete("instances/{id:guid}")]
    public async Task<IActionResult> DeleteInstance(Guid id, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct);
        if (instance == null) return NotFound();

        instanceRepo.SoftDelete(instance);
        await instanceRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── POST /api/v1/connectors/instances/{id}/sync ──────────────────────────
    [HttpPost("instances/{id:guid}/sync")]
    public async Task<IActionResult> TriggerSync(Guid id, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct);
        if (instance == null) return NotFound();

        if (instance.Status != ConnectorInstanceStatus.Active)
            return UnprocessableEntity("Le connecteur doit être actif pour lancer une synchronisation.");

        instance.RecordSync();
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);

        var executionId = Guid.CreateVersion7().ToString("N");
        return Ok200(new SyncRunResult(executionId, $"/api/v1/connectors/instances/{id}/status/{executionId}"));
    }

    // ── POST /api/v1/connectors/instances/{id}/test ───────────────────────────
    [HttpPost("instances/{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct);
        if (instance == null) return NotFound();

        // Stub — en production : appel réel au connecteur pour tester la connexion
        return Ok200(new ConnectionTestResult(
            ConnectionSuccessful: true,
            LatencyMs:            45,
            AccessibleItems:      0,
            Permissions:          ["read", "write"],
            ErrorMessage:         null));
    }

    // ── Sync Jobs ─────────────────────────────────────────────────────────────

    // ── GET /api/v1/connectors/instances/{id}/jobs ────────────────────────────
    [HttpGet("instances/{id:guid}/jobs")]
    public async Task<IActionResult> ListSyncJobs(Guid id, CancellationToken ct)
    {
        var jobs = await syncJobRepo.GetByInstanceAsync(id, ct);
        return Ok200(jobs.Select(MapJob).ToList());
    }

    // ── POST /api/v1/connectors/instances/{id}/jobs ───────────────────────────
    [HttpPost("instances/{id:guid}/jobs")]
    public async Task<IActionResult> CreateSyncJob(
        Guid id,
        [FromBody] CreateSyncJobRequest req,
        CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var instance = await instanceRepo.GetByIdAsync(id, ct);
        if (instance == null) return NotFound();

        var job = SyncJob.Create(
            TenantId, id, req.Name, req.Direction,
            ActorId.Value, req.CronExpression);

        await syncJobRepo.AddAsync(job, ct);
        await syncJobRepo.SaveAsync(ct);

        return Ok200(MapJob(job));
    }

    // ── DELETE /api/v1/connectors/instances/{instanceId}/jobs/{jobId} ─────────
    [HttpDelete("instances/{instanceId:guid}/jobs/{jobId:guid}")]
    public async Task<IActionResult> DeleteSyncJob(Guid instanceId, Guid jobId, CancellationToken ct)
    {
        var job = await syncJobRepo.GetByIdAsync(jobId, ct);
        if (job == null || job.ConnectorInstanceId != instanceId) return NotFound();

        syncJobRepo.SoftDelete(job);
        await syncJobRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static ConnectorInstanceDto MapInstance(ConnectorInstance i) =>
        new(i.Id, i.DefinitionId, "Connector", null, i.Name, i.Description,
            i.Status, i.Health, i.LastHealthCheckMessage, i.LastSyncAt, i.CreatedAt);

    private static SyncJobDto MapJob(SyncJob j) =>
        new(j.Id, j.Name, j.Direction, j.Status, j.CronExpression,
            j.NextRunAt, j.LastRunAt, j.LastRunResult, j.TotalSynced, j.CreatedAt);
}
