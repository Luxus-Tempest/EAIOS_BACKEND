using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Application.Connector;
using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[Route("api/v1/connectors")]
public sealed class ConnectorsController(
    IConnectorInstanceRepository instanceRepository,
    ISyncJobRepository syncJobRepository,
    Application.Common.Interfaces.ICurrentUser currentUser) : V1ApiController
{
    [HttpGet("instances")]
    public async Task<IActionResult> ListInstances(CancellationToken ct)
    {
        var instances = await instanceRepository.GetAllAsync(ct);
        var dtos = instances.Select(i => new ConnectorInstanceDto(
            i.Id, i.DefinitionId, "SharePoint Connector", null, i.Name, i.Description,
            i.Status, i.Health, i.LastHealthCheckMessage, i.LastSyncAt, i.CreatedAt)).ToList();
        return Ok(ApiResponse.Wrap(dtos));
    }

    [HttpPost("instances")]
    public async Task<IActionResult> CreateInstance([FromBody] CreateConnectorRequest request, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return Unauthorized();
        var orgId = currentUser.OrganizationId ?? Guid.Empty;

        var instance = ConnectorInstance.Create(orgId, request.DefinitionId, request.Name, currentUser.UserId.Value, description: request.Description);
        await instanceRepository.AddAsync(instance, ct);
        await instanceRepository.SaveAsync(ct);

        return Ok(ApiResponse.Wrap(instance.Id));
    }

    [HttpPost("instances/{id:guid}/sync")]
    public async Task<IActionResult> TriggerSync(Guid id, CancellationToken ct)
    {
        var instance = await instanceRepository.GetByIdAsync(id, ct);
        if (instance == null) return NotFound();

        instance.RecordSync();
        await instanceRepository.SaveAsync(ct);

        return Ok(ApiResponse.Wrap(new SyncRunResult(Guid.CreateVersion7().ToString("N"), $"/api/v1/connectors/instances/{id}/status")));
    }
}
