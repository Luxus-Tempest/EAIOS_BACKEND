using EAIOS.Api.Domain.Search;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using System.Diagnostics;

namespace EAIOS.Api.Application.Search;

/// <summary>
/// Recherche hybride sur les documents et la base de connaissance.
///
/// Chaque recherche est tracée dans le journal analytique : c'est cette écriture
/// qui alimente <c>IAnalyticsQueryService.GetSearchAnalyticsAsync</c> (requêtes
/// populaires, recherches sans résultat, nombre moyen de résultats).
/// </summary>
public sealed class SearchService(
    ISavedSearchRepository savedSearchRepo,
    IDocumentRepository documentRepo,
    IKnowledgeItemRepository knowledgeRepo,
    IAnalyticsTracker analytics,
    ILlmService llm) : ISearchService
{
    private const float DocumentBaseScore  = 0.80f;
    private const float KnowledgeBaseScore = 0.75f;

    public async Task<object> SearchAsync(Guid tenantId, Guid actorId, SearchRequest req, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var docQuery = new DocumentQuery(req.Query,
            WorkspaceId: req.Filters?.WorkspaceId, Page: req.Page, PageSize: req.PageSize);

        var docs  = await documentRepo.SearchAsync(docQuery, ct);
        var items = await knowledgeRepo.SearchAsync(
            req.Query, null, Domain.Knowledge.KnowledgeItemStatus.Published, null, req.Page, req.PageSize, ct);

        var results = new List<SearchHit>(docs.Items.Count + items.Items.Count);

        results.AddRange(docs.Items.Select(d => new SearchHit(
            Id:        d.Id,
            Type:      "document",
            Title:     d.Title,
            Summary:   string.IsNullOrWhiteSpace(d.Description) ? $"Document • {d.MimeType}" : d.Description,
            CreatedAt: d.CreatedAt,
            Score:     ScoreFor(req.Query, d.Title, DocumentBaseScore))));

        results.AddRange(items.Items.Select(k => new SearchHit(
            Id:        k.Id,
            Type:      "knowledge",
            Title:     k.Title,
            Summary:   string.IsNullOrWhiteSpace(k.Summary) ? $"Connaissance • {k.Type}" : k.Summary,
            CreatedAt: k.CreatedAt,
            Score:     ScoreFor(req.Query, k.Title, KnowledgeBaseScore))));

        var ranked = results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.CreatedAt)
            .Take(req.PageSize)
            .ToList();

        var total = docs.TotalCount + items.TotalCount;
        stopwatch.Stop();

        // Une recherche sans résultat est tracée séparément : elle révèle un
        // manque de contenu ou un vocabulaire non couvert.
        await analytics.TrackAsync(
            total == 0 ? AnalyticsEventTypes.SearchZeroResults : AnalyticsEventTypes.SearchExecuted,
            resourceType: "Search",
            durationMs:   stopwatch.ElapsedMilliseconds,
            properties: new
            {
                query       = req.Query,
                resultCount = total,
                topScore    = ranked.Count > 0 ? ranked[0].Score : 0f,
                page        = req.Page
            },
            workspaceId: req.Filters?.WorkspaceId,
            ct: ct);

        return new
        {
            Items      = ranked,
            TotalCount = total,
            Page       = req.Page,
            PageSize   = req.PageSize,
            TookMs     = stopwatch.ElapsedMilliseconds
        };
    }

    public async Task<object> SuggestAsync(Guid tenantId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Array.Empty<string>();

        var docs  = await documentRepo.SearchAsync(new DocumentQuery(query, Page: 1, PageSize: 5), ct);
        var items = await knowledgeRepo.SearchAsync(query, null, null, null, 1, 5, ct);

        return docs.Items.Select(d => d.Title)
            .Concat(items.Items.Select(k => k.Title))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    public async Task<object> AskAsync(Guid tenantId, Guid actorId, AskRequest req, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var items = await knowledgeRepo.SearchAsync(
            req.Question, null, Domain.Knowledge.KnowledgeItemStatus.Published, null, 1, 5, ct);

        var context = string.Join("\n\n---\n\n", items.Items.Select(i => $"### {i.Title}\n{i.Content}"));

        var systemPrompt = $"""
            Tu es EAIOS, un assistant IA enterprise intelligent.
            Tu réponds EN FRANÇAIS uniquement sur la base du contexte fourni.
            Si la réponse n'est pas disponible, dis-le clairement.

            CONTEXTE DISPONIBLE:
            {(string.IsNullOrWhiteSpace(context) ? "Aucun contexte disponible." : context)}
            """;

        var result = await llm.GenerateAsync(systemPrompt, req.Question, null, ct);
        stopwatch.Stop();

        await analytics.TrackAsync(AnalyticsEventTypes.SearchAsked,
            resourceType: "Search",
            durationMs:   stopwatch.ElapsedMilliseconds,
            properties: new
            {
                query       = req.Question,
                resultCount = items.Items.Count,
                model       = result.ModelUsed,
                tokens      = result.TotalTokens
            },
            ct: ct);

        return new
        {
            Answer   = result.Output,
            Sources  = items.Items.Select(i => new { i.Id, i.Title, i.Type }),
            Metadata = new { result.PromptTokens, result.CompletionTokens, result.ModelUsed, TookMs = stopwatch.ElapsedMilliseconds }
        };
    }

    public async Task<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(Guid actorId, CancellationToken ct = default) =>
        await savedSearchRepo.GetByUserAsync(actorId, ct);

    public async Task<SavedSearch> SaveSearchAsync(
        Guid tenantId, Guid actorId, SaveSearchRequest req, CancellationToken ct = default)
    {
        var saved = SavedSearch.Create(tenantId, actorId, req.Name, req.Query, SearchType.Basic, req.Filters, isShared: req.IsShared);
        await savedSearchRepo.AddAsync(saved, ct);
        await savedSearchRepo.SaveAsync(ct);
        return saved;
    }

    public async Task DeleteSavedSearchAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var search = await savedSearchRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Recherche sauvegardée introuvable.");

        if (search.UserId != actorId)
            throw new KeyNotFoundException("Recherche sauvegardée introuvable pour cet utilisateur.");

        savedSearchRepo.SoftDelete(search);
        await savedSearchRepo.SaveAsync(ct);
    }

    /// <summary>
    /// Score de pertinence lexical simple : un titre qui contient la requête entière
    /// prime sur une correspondance partielle. Suffisant pour un tri stable en attendant
    /// un moteur vectoriel ; sans cela tous les résultats d'un même type étaient ex aequo.
    /// </summary>
    private static float ScoreFor(string? query, string? title, float baseScore)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(title))
            return baseScore;

        var q = query.Trim();

        if (title.Equals(q, StringComparison.OrdinalIgnoreCase))
            return Math.Min(1f, baseScore + 0.20f);

        if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return Math.Min(1f, baseScore + 0.12f);

        if (title.Contains(q, StringComparison.OrdinalIgnoreCase))
            return Math.Min(1f, baseScore + 0.06f);

        // Correspondance partielle : proportion des termes de la requête présents dans le titre.
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return baseScore;

        var hits = terms.Count(t => title.Contains(t, StringComparison.OrdinalIgnoreCase));
        return baseScore + 0.06f * ((float)hits / terms.Length);
    }
}

/// <summary>Résultat unifié document / connaissance renvoyé par la recherche.</summary>
public sealed record SearchHit(
    Guid Id,
    string Type,
    string Title,
    string? Summary,
    DateTime CreatedAt,
    float Score);
