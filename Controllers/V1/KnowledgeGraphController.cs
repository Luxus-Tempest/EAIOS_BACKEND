using EAIOS.Api.Application.Knowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Exploration du graphe de connaissances : entités, relations, traversée, chemins, requêtes.
/// Route : /api/v1/graph
/// </summary>
[Route("api/v1/graph")]
[Authorize]
public sealed class KnowledgeGraphController(
    IKnowledgeGraphService graphService) : V1ApiController
{
    // ── Entités ───────────────────────────────────────────────────────────────

    [HttpGet("entities/{id:guid}")]
    public async Task<IActionResult> GetEntity(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok200(await graphService.GetEntityAsync(id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Entité introuvable dans le graphe.");
        }
    }

    [HttpGet("entities/{id:guid}/relations")]
    public async Task<IActionResult> GetRelations(Guid id, CancellationToken ct)
    {
        try
        {
            // GetEntityAsync lève KeyNotFoundException si l'entité n'existe pas :
            // on évite ainsi de renvoyer une liste vide pour un identifiant inconnu.
            await graphService.GetEntityAsync(id, ct);
            return Ok200(await graphService.GetRelationsAsync(id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Entité introuvable dans le graphe.");
        }
    }

    // ── Traversée ─────────────────────────────────────────────────────────────

    /// <summary>Sous-graphe autour d'une entité (parcours en largeur borné).</summary>
    [HttpGet("entities/{id:guid}/subgraph")]
    public async Task<IActionResult> GetSubgraph(
        Guid id,
        [FromQuery] int depth = 2,
        [FromQuery] string direction = "both",
        [FromQuery] string? types = null,
        [FromQuery] int maxNodes = 200,
        CancellationToken ct = default)
    {
        var dir = direction.ToLowerInvariant() switch
        {
            "out" or "outgoing" => GraphDirection.Outgoing,
            "in"  or "incoming" => GraphDirection.Incoming,
            _                   => GraphDirection.Both
        };

        var typeArray = types?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            var result = await graphService.GetSubgraphAsync(id, depth, dir, typeArray, maxNodes, ct);
            return Ok200(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Entité racine introuvable dans le graphe.");
        }
    }

    /// <summary>Plus court chemin entre deux entités.</summary>
    [HttpGet("path")]
    public async Task<IActionResult> GetPath(
        [FromQuery] Guid from,
        [FromQuery] Guid to,
        [FromQuery] int maxDepth = 5,
        CancellationToken ct = default)
    {
        try
        {
            var path = await graphService.FindPathAsync(from, to, maxDepth, ct);
            return path is null
                ? Ok200(new { Found = false, Path = (GraphPathDto?)null })
                : Ok200(new { Found = true, Path = path });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // ── Relations (CRUD) ──────────────────────────────────────────────────────

    [HttpPost("relations")]
    public async Task<IActionResult> CreateRelation([FromBody] CreateRelationRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var relation = await graphService.CreateRelationAsync(
                TenantId, req.SourceItemId, req.TargetItemId, req.RelationType,
                ActorId.Value, req.Label, req.ConfidenceScore, ct);

            return Ok200(new GraphRelationDto(
                relation.Id.ToString(),
                relation.SourceItemId.ToString(),
                relation.TargetItemId.ToString(),
                relation.RelationType,
                relation.ConfidenceScore));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpDelete("relations/{id:guid}")]
    public async Task<IActionResult> DeleteRelation(Guid id, CancellationToken ct)
    {
        try
        {
            await graphService.DeleteRelationAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Relation introuvable.");
        }
    }

    // ── Requêtes ──────────────────────────────────────────────────────────────

    [HttpPost("query")]
    public async Task<IActionResult> ExecuteQuery([FromBody] GraphQueryRequest req, CancellationToken ct)
    {
        try
        {
            return Ok200(await graphService.ExecuteGraphQueryAsync(req.Query, req.Parameters, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "INVALID_QUERY", message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
