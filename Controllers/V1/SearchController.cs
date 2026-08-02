using EAIOS.Api.Application.Search;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Recherche hybride (lexicale + sémantique), searches sauvegardées, suggestions.
/// </summary>
[Route("api/v1/search")]
public sealed class SearchController(
    ISearchService            searchService) : V1ApiController
{
    // ── POST /api/v1/search ───────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var result = await searchService.SearchAsync(TenantId, ActorId.Value, req, ct);
        return Ok200(result);
    }

    // ── POST /api/v1/search/suggestions ──────────────────────────────────────

    [HttpPost("suggestions")]
    public async Task<IActionResult> Suggest([FromBody] SuggestRequest req, CancellationToken ct)
    {
        var suggestions = await searchService.SuggestAsync(TenantId, req.Query, ct);
        return Ok200(suggestions);
    }

    // ── POST /api/v1/search/ask (RAG unifié) ─────────────────────────────────

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var result = await searchService.AskAsync(TenantId, ActorId.Value, req, ct);
        return Ok200(result);
    }

    // ── Saved Searches ────────────────────────────────────────────────────────

    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedSearches(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var searches = await searchService.GetSavedSearchesAsync(ActorId.Value, ct);
        return Ok200(searches.Select(MapSaved).ToList());
    }

    [HttpPost("saved")]
    public async Task<IActionResult> SaveSearch([FromBody] SaveSearchRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var saved = await searchService.SaveSearchAsync(TenantId, ActorId.Value, req, ct);
        return Ok200(MapSaved(saved));
    }

    [HttpDelete("saved/{id:guid}")]
    public async Task<IActionResult> DeleteSavedSearch(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            await searchService.DeleteSavedSearchAsync(id, ActorId.Value, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapSaved(SavedSearch s) => new
    {
        s.Id, s.Name, s.QueryText, s.IsShared, s.LastExecutedAt, s.CreatedAt
    };
}
