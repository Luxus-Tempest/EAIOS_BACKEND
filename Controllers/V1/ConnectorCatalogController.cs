using EAIOS.Api.Application.Connector;
using EAIOS.Api.Domain.Connector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Catalogue des connecteurs disponibles sur la plateforme.
/// Route : /api/v1/connectors/catalog
/// </summary>
[Route("api/v1/connectors/catalog")]
[Authorize]
public sealed class ConnectorCatalogController(
    IConnectorCatalogService catalogService) : V1ApiController
{
    [HttpGet]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
    {
        var catalog = await catalogService.GetCatalogAsync(ct);
        return Ok200(catalog.Select(MapDefinition).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDefinition(Guid id, CancellationToken ct)
    {
        try
        {
            var def = await catalogService.GetDefinitionAsync(id, ct);
            return Ok200(MapDefinition(def));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static ConnectorDefinitionDto MapDefinition(ConnectorDefinition d) =>
        new(d.Id, d.Name, d.Slug, d.Description, d.LogoUrl, d.Category,
            d.AuthType, d.SupportedCapabilities, d.Version, d.IsActive);
}
