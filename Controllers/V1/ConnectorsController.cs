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
    IConnectorService            connectorService,
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

        var instance = await connectorService.CreateInstanceAsync(
            TenantId, req.DefinitionId, req.Name, req.Description, req.WorkspaceId, ActorId.Value, ct);

        return Created201("GetConnectorInstance", new { id = instance.Id }, MapInstance(instance));
    }

    // ── PUT /api/v1/connectors/instances/{id} ────────────────────────────────
    [HttpPut("instances/{id:guid}")]
    public async Task<IActionResult> UpdateInstance(
        Guid id,
        [FromBody] UpdateConnectorRequest req,
        CancellationToken ct)
    {
        try
        {
            var instance = await connectorService.UpdateInstanceAsync(id, req.Name, req.Description, ct);
            return Ok200(MapInstance(instance));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── DELETE /api/v1/connectors/instances/{id} ──────────────────────────────
    [HttpDelete("instances/{id:guid}")]
    public async Task<IActionResult> DeleteInstance(Guid id, CancellationToken ct)
    {
        try
        {
            await connectorService.DeleteInstanceAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── POST /api/v1/connectors/instances/{id}/sync ──────────────────────────
    [HttpPost("instances/{id:guid}/sync")]
    public async Task<IActionResult> TriggerSync(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await connectorService.TriggerSyncAsync(id, ct);
            return Ok200(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    // ── POST /api/v1/connectors/instances/{id}/test ───────────────────────────
    [HttpPost("instances/{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await connectorService.TestConnectionAsync(id, ct);
            return Ok200(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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

        try
        {
            var job = await connectorService.CreateSyncJobAsync(
                TenantId, id, req.Name, req.Direction, req.CronExpression, ActorId.Value, ct);

            return Ok200(MapJob(job));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── DELETE /api/v1/connectors/instances/{instanceId}/jobs/{jobId} ─────────
    [HttpDelete("instances/{instanceId:guid}/jobs/{jobId:guid}")]
    public async Task<IActionResult> DeleteSyncJob(Guid instanceId, Guid jobId, CancellationToken ct)
    {
        try
        {
            await connectorService.DeleteSyncJobAsync(instanceId, jobId, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static ConnectorInstanceDto MapInstance(ConnectorInstance i) =>
        new(i.Id, i.DefinitionId, "Connector", null, i.Name, i.Description,
            i.Status, i.Health, i.LastHealthCheckMessage, i.LastSyncAt, i.CreatedAt);

    private static SyncJobDto MapJob(SyncJob j) =>
        new(j.Id, j.Name, j.Direction, j.Status, j.CronExpression,
            j.NextRunAt, j.LastRunAt, j.LastRunResult, j.TotalSynced, j.CreatedAt);
}
