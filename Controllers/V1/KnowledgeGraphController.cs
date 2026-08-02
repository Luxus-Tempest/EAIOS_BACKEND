using EAIOS.Api.Application.Knowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Exploration du graphe de connaissances : entités, relations, requêtes GraphQL-like/Gremlin.
/// Route : /api/v1/graph
/// </summary>
[Route("api/v1/graph")]
[Authorize]
public sealed class KnowledgeGraphController(
    IKnowledgeGraphService graphService) : V1ApiController
{
    // ── Entités et Relations ──────────────────────────────────────────────────

    [HttpGet("entities/{id:guid}")]
    public async Task<IActionResult> GetEntity(Guid id, CancellationToken ct)
    {
        try
        {
            var entity = await graphService.GetEntityAsync(id, ct);
            return Ok200(entity);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("entities/{id:guid}/relations")]
    public async Task<IActionResult> GetRelations(Guid id, CancellationToken ct)
    {
        // On vérifie d'abord si l'entité existe (GetEntity lancera KeyNotFoundException si non)
        try
        {
            await graphService.GetEntityAsync(id, ct);
            var relations = await graphService.GetRelationsAsync(id, ct);
            return Ok200(relations);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Requêtes Graphe ───────────────────────────────────────────────────────

    [HttpPost("query")]
    public async Task<IActionResult> ExecuteQuery([FromBody] GraphQueryRequest req, CancellationToken ct)
    {
        try
        {
            var result = await graphService.ExecuteGraphQueryAsync(req.Query, req.Parameters, ct);
            return Ok200(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "INVALID_QUERY", message = ex.Message });
        }
    }
}
