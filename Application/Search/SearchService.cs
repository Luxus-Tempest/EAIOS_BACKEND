using System.Diagnostics;
using System.Text.RegularExpressions;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Security;

namespace EAIOS.Api.Application.Search;

/// <summary>
/// Recherche hybride sur les documents et la base de connaissance.
///
/// <para>
/// Deux classements, fusionnés : l'index <b>plein texte</b> de PostgreSQL
/// (titre, description, texte extrait, contenu des fiches) et la recherche
/// <b>sémantique</b> du runtime (segments vectorisés, dans le périmètre de la
/// portée signée). La fusion est une fusion de rangs réciproques : un résultat
/// bien classé des deux côtés remonte, un résultat que seul un côté connaît
/// reste visible. C'est ce qui remplace la comparaison de la requête au seul
/// titre, qui tenait lieu de recherche jusqu'ici.
/// </para>
/// <para>
/// Chaque recherche est tracée dans le journal analytique : c'est cette écriture
/// qui alimente <c>IAnalyticsQueryService.GetSearchAnalyticsAsync</c>.
/// </para>
/// </summary>
public sealed class SearchService(
    ISavedSearchRepository savedSearchRepo,
    IDocumentRepository documentRepo,
    IKnowledgeItemRepository knowledgeRepo,
    IAnalyticsTracker analytics,
    EAIOS.Api.Application.Knowledge.IKnowledgeService knowledge,
    IAgentContextService contextService,
    IAgentRuntimeClient runtime,
    IHttpContextAccessor httpContextAccessor,
    EAIOS.Api.Application.Resource.IDocumentAccessService access,
    ILogger<SearchService> logger) : ISearchService
{
    /// <summary>Constante de la fusion de rangs réciproques : 60 est la valeur de la littérature.</summary>
    private const int RrfK = 60;

    /// <summary>Profondeur de chaque classement avant fusion.</summary>
    private const int Depth = 60;

    public async Task<object> SearchAsync(Guid tenantId, Guid actorId, SearchRequest req, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var query = req.Query?.Trim() ?? "";

        // ── 1. Plein texte, dans ce que la personne peut voir ─────────────────
        var visibility = await access.GetVisibilityAsync(actorId, ct);
        var ceiling = visibility.Unrestricted ? (ResourceClassification?)null : visibility.MaxClassification;
        var docs  = await documentRepo.FullTextSearchAsync(query, req.Filters?.WorkspaceId, req.Filters?.Classification, Depth,
            ceiling, visibility.ExplicitlyAllowed, visibility.ExplicitlyDenied, ct);
        var items = await knowledgeRepo.FullTextSearchAsync(query, KnowledgeItemStatus.Published, Depth, ct, ceiling);

        // ── 2. Sémantique, dans le périmètre de la personne ───────────────────
        var semantic = Array.Empty<RuntimeSearchHit>() as IReadOnlyList<RuntimeSearchHit>;
        var mode = "hybrid";
        if (req.Type != SearchType.Basic && query.Length > 0)
        {
            try
            {
                var context = contextService.IssueForAssistant(tenantId, actorId, [], Guid.CreateVersion7());
                semantic = await runtime.SearchAsync(context.Token, CallerToken(), query, Depth, ct);
            }
            catch (Exception ex) when (ex is AgentRuntimeUnavailableException or InvalidOperationException)
            {
                // La recherche reste utile sans vecteurs, mais elle le dit.
                logger.LogWarning(ex, "Runtime injoignable : recherche lexicale seule.");
                mode = "lexical-degraded";
            }
        }
        else
        {
            mode = "lexical";
        }

        // ── 3. Fusion ─────────────────────────────────────────────────────────
        var fused = new Dictionary<string, Fused>();

        void Vote(string key, int rank, Func<Fused> create, Action<Fused>? enrich = null)
        {
            if (!fused.TryGetValue(key, out var entry))
                fused[key] = entry = create();
            entry.Score += 1.0 / (RrfK + rank);
            enrich?.Invoke(entry);
        }

        for (var i = 0; i < docs.Count; i++)
        {
            var d = docs[i];
            Vote($"document:{d.Id}", i + 1, () => new Fused(d.Id, "document", d.Title, d.CreatedAt)
            {
                Summary = Excerpt(query, d.Description, d.ExtractedText),
                Classification = d.Classification.ToString(),
                MimeType = d.MimeType,
                DocumentId = d.Id,
                Lexical = true,
            });
        }

        for (var i = 0; i < items.Count; i++)
        {
            var k = items[i];
            var key = k.SourceDocumentId is { } sourceId && k.Source == KnowledgeItemSource.AutoExtracted
                ? $"document:{sourceId}"
                : $"knowledge:{k.Id}";
            Vote(key, i + 1, () => new Fused(k.SourceDocumentId ?? k.Id, k.SourceDocumentId is null ? "knowledge" : "document", k.Title, k.CreatedAt)
            {
                Summary = Excerpt(query, k.Summary, k.Content),
                DocumentId = k.SourceDocumentId,
                KnowledgeItemId = k.Id,
                Lexical = true,
            }, entry =>
            {
                entry.KnowledgeItemId ??= k.Id;
                entry.Summary ??= Excerpt(query, k.Summary, k.Content);
            });
        }

        for (var i = 0; i < semantic.Count; i++)
        {
            var hit = semantic[i];
            var key = hit.DocumentId is { } docId ? $"document:{docId}" : $"knowledge:{hit.ItemId}";
            if (hit.ItemId is null && hit.DocumentId is null) continue;

            Vote(key, i + 1, () => new Fused(hit.DocumentId ?? hit.ItemId!.Value, hit.DocumentId is null ? "knowledge" : "document", hit.Title, DateTime.UtcNow)
            {
                Summary = hit.Excerpt,
                DocumentId = hit.DocumentId,
                KnowledgeItemId = hit.ItemId,
                Page = hit.Page,
                Reference = hit.Reference,
                Semantic = true,
            }, entry =>
            {
                // L'extrait sémantique dit *où* la requête a été comprise :
                // il prime sur un début de description.
                entry.Semantic = true;
                entry.Page ??= hit.Page;
                entry.Reference ??= hit.Reference;
                if (!string.IsNullOrWhiteSpace(hit.Excerpt)) entry.Summary = hit.Excerpt;
            });
        }

        var ranked = fused.Values.OrderByDescending(f => f.Score).ThenByDescending(f => f.CreatedAt).ToList();
        var maxScore = ranked.Count > 0 ? ranked[0].Score : 1.0;

        var total = ranked.Count;
        var page = ranked
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .Select(f => new SearchHit(
                Id: f.Id,
                Type: f.Type,
                Title: f.Title,
                Summary: f.Summary,
                CreatedAt: f.CreatedAt,
                // Score normalisé sur le premier résultat : lisible comme une pertinence relative.
                Score: (float)Math.Round(f.Score / maxScore, 3),
                Page: f.Page,
                Reference: f.Reference,
                DocumentId: f.DocumentId,
                KnowledgeItemId: f.KnowledgeItemId,
                Classification: f.Classification,
                MimeType: f.MimeType,
                MatchedBy: f.Lexical && f.Semantic ? "both" : f.Semantic ? "semantic" : "lexical"))
            .ToList();

        // ── 4. Facettes, sur l'ensemble fusionné ──────────────────────────────
        var facets = new Dictionary<string, IReadOnlyList<FacetValue>>
        {
            ["type"] = ranked.GroupBy(f => f.Type).Select(g => new FacetValue(g.Key, g.Count())).ToList(),
            ["classification"] = ranked.Where(f => f.Classification is not null)
                .GroupBy(f => f.Classification!).Select(g => new FacetValue(g.Key, g.Count())).ToList(),
        };

        stopwatch.Stop();

        // Une recherche sans résultat est tracée séparément : elle révèle un
        // manque de contenu ou un vocabulaire non couvert.
        await analytics.TrackAsync(
            total == 0 ? AnalyticsEventTypes.SearchZeroResults : AnalyticsEventTypes.SearchExecuted,
            resourceType: "Search",
            durationMs:   stopwatch.ElapsedMilliseconds,
            properties: new
            {
                query,
                resultCount = total,
                topScore    = page.Count > 0 ? page[0].Score : 0f,
                page        = req.Page,
                mode
            },
            workspaceId: req.Filters?.WorkspaceId,
            ct: ct);

        return new
        {
            Items      = page,
            TotalCount = total,
            Page       = req.Page,
            PageSize   = req.PageSize,
            TookMs     = stopwatch.ElapsedMilliseconds,
            Mode       = mode,
            Facets     = facets
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

    /// <summary>
    /// Question posée depuis la recherche.
    ///
    /// <para>
    /// Délègue au <b>même</b> chemin que <c>/knowledge/ask</c>. Il n'existe
    /// qu'une seule façon de répondre à une question documentaire, et la forme
    /// de la réponse est préservée pour ne rien casser côté frontend.
    /// </para>
    /// </summary>
    public async Task<object> AskAsync(Guid tenantId, Guid actorId, AskRequest req, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var answer = await knowledge.AskAsync(req.Question, packId: null, ct);
        stopwatch.Stop();

        await analytics.TrackAsync(AnalyticsEventTypes.SearchAsked,
            resourceType: "Search",
            durationMs:   stopwatch.ElapsedMilliseconds,
            properties: new
            {
                query       = req.Question,
                resultCount = answer.Sources.Count,
                mode        = answer.RetrievalMode,
                tokens      = answer.PromptTokens + answer.CompletionTokens
            },
            ct: ct);

        return new
        {
            answer.Answer,
            Sources  = answer.Sources.Select(s => new { s.Id, s.Title, s.Type }),
            // Les citations arrivent en plus : la forme historique est intacte,
            // les consommateurs existants ne voient aucun changement.
            answer.Citations,
            answer.Unresolved,
            Metadata = new
            {
                answer.PromptTokens,
                answer.CompletionTokens,
                answer.RetrievalMode,
                TookMs = stopwatch.ElapsedMilliseconds
            }
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Un extrait autour de la première occurrence d'un terme de la requête,
    /// sinon le début du texte. Sans texte, la description ; sans rien, null.
    /// </summary>
    private static string? Excerpt(string query, string? description, string? text)
    {
        var body = string.IsNullOrWhiteSpace(text) ? description : text;
        if (string.IsNullOrWhiteSpace(body)) return description;

        var flat = Regex.Replace(body, @"\s+", " ").Trim();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2).ToList();

        var at = -1;
        foreach (var term in terms)
        {
            at = flat.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) break;
        }

        const int window = 220;
        if (at < 0) return flat.Length <= window ? flat : flat[..window].TrimEnd() + "…";

        var start = Math.Max(0, at - window / 3);
        var end = Math.Min(flat.Length, start + window);
        var slice = flat[start..end].Trim();
        return (start > 0 ? "…" : "") + slice + (end < flat.Length ? "…" : "");
    }

    private string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MISSING_CALLER_TOKEN");

        return header["Bearer ".Length..].Trim();
    }

    private sealed class Fused(Guid id, string type, string title, DateTime createdAt)
    {
        public Guid Id { get; } = id;
        public string Type { get; } = type;
        public string Title { get; } = title;
        public DateTime CreatedAt { get; } = createdAt;
        public double Score { get; set; }
        public string? Summary { get; set; }
        public string? Classification { get; set; }
        public string? MimeType { get; set; }
        public Guid? DocumentId { get; set; }
        public Guid? KnowledgeItemId { get; set; }
        public int? Page { get; set; }
        public string? Reference { get; set; }
        public bool Lexical { get; set; }
        public bool Semantic { get; set; }
    }
}

/// <summary>Résultat unifié document / connaissance renvoyé par la recherche.</summary>
public sealed record SearchHit(
    Guid Id,
    string Type,
    string Title,
    string? Summary,
    DateTime CreatedAt,
    float Score,
    int? Page = null,
    string? Reference = null,
    Guid? DocumentId = null,
    Guid? KnowledgeItemId = null,
    string? Classification = null,
    string? MimeType = null,
    /// <summary>« lexical », « semantic » ou « both » : ce qui a fait remonter le résultat.</summary>
    string MatchedBy = "lexical");
