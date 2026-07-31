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
    ISavedSearchRepository    savedSearchRepo,
    IDocumentRepository       documentRepo,
    IKnowledgeItemRepository  knowledgeRepo,
    ILlmService               llm) : V1ApiController
{
    // ── POST /api/v1/search ───────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var docQuery = new DocumentQuery(req.Query, WorkspaceId: req.Filters?.WorkspaceId, Page: req.Page, PageSize: req.PageSize);
        var docs  = await documentRepo.SearchAsync(docQuery, ct);
        var items = await knowledgeRepo.SearchAsync(req.Query, null, Domain.Knowledge.KnowledgeItemStatus.Published, null, req.Page, req.PageSize, ct);

        var results = new List<dynamic>();

        results.AddRange(docs.Items.Select(d => new {
            Id = d.Id, Type = "document", Title = d.Title, Summary = $"Document • {d.MimeType}", CreatedAt = d.CreatedAt, Score = 0.8f }));

        results.AddRange(items.Items.Select(k => new {
            Id = k.Id, Type = "knowledge", Title = k.Title, Summary = $"Connaissance • {k.Type}", CreatedAt = k.CreatedAt, Score = 0.75f }));

        var sortedResults = results.OrderByDescending(r => r.Score).Take(req.PageSize).ToList();

        var total = docs.TotalCount + items.TotalCount;

        // Incrémenter compteur si saved search
        // (à implémenter avec background service en production)

        return OkList(sortedResults, total, req.Page, req.PageSize);
    }

    // ── POST /api/v1/search/suggestions ──────────────────────────────────────

    [HttpPost("suggestions")]
    public async Task<IActionResult> Suggest([FromBody] SuggestRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Query) || req.Query.Length < 2)
            return Ok200(Array.Empty<string>());

        // Suggestions depuis les titres de documents et items de connaissance
        var docs  = await documentRepo.SearchAsync(new DocumentQuery(req.Query, Page: 1, PageSize: 5), ct);
        var items = await knowledgeRepo.SearchAsync(req.Query, null, null, null, 1, 5, ct);

        var suggestions = docs.Items.Select(d => d.Title)
            .Concat(items.Items.Select(k => k.Title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return Ok200(suggestions);
    }

    // ── POST /api/v1/search/ask (RAG unifié) ─────────────────────────────────

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var items = await knowledgeRepo.SearchAsync(req.Question, null, Domain.Knowledge.KnowledgeItemStatus.Published, null, 1, 5, ct);
        var context = string.Join("\n\n---\n\n", items.Items.Select(i => $"### {i.Title}\n{i.Content}"));

        var systemPrompt = $"""
            Tu es EAIOS, un assistant IA enterprise intelligent. 
            Tu réponds EN FRANÇAIS uniquement sur la base du contexte fourni.
            Si la réponse n'est pas disponible, dis-le clairement.
            
            CONTEXTE DISPONIBLE:
            {(string.IsNullOrWhiteSpace(context) ? "Aucun contexte disponible." : context)}
            """;

        var result = await llm.GenerateAsync(systemPrompt, req.Question, null, ct);

        return Ok200(new
        {
            Answer   = result.Output,
            Sources  = items.Items.Select(i => new { i.Id, i.Title, i.Type }),
            Metadata = new { result.PromptTokens, result.CompletionTokens, result.ModelUsed }
        });
    }

    // ── Saved Searches ────────────────────────────────────────────────────────

    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedSearches(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var searches = await savedSearchRepo.GetByUserAsync(ActorId.Value, ct);
        return Ok200(searches.Select(MapSaved).ToList());
    }

    [HttpPost("saved")]
    public async Task<IActionResult> SaveSearch([FromBody] SaveSearchRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var saved = SavedSearch.Create(TenantId, ActorId.Value, req.Name, req.Query, Domain.Search.SearchType.Basic, req.Filters, isShared: req.IsShared);
        await savedSearchRepo.AddAsync(saved, ct);
        await savedSearchRepo.SaveAsync(ct);
        return Ok200(MapSaved(saved));
    }

    [HttpDelete("saved/{id:guid}")]
    public async Task<IActionResult> DeleteSavedSearch(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var search = await savedSearchRepo.GetByIdAsync(id, ct);
        if (search == null || search.UserId != ActorId.Value) return NotFound();
        savedSearchRepo.SoftDelete(search);
        await savedSearchRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapSaved(SavedSearch s) => new
    {
        s.Id, s.Name, s.QueryText, s.IsShared, s.LastExecutedAt, s.CreatedAt
    };
}
