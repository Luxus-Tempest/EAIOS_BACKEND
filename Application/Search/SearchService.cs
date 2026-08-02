using EAIOS.Api.Domain.Search;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;

namespace EAIOS.Api.Application.Search;

public sealed class SearchService(
    ISavedSearchRepository savedSearchRepo,
    IDocumentRepository documentRepo,
    IKnowledgeItemRepository knowledgeRepo,
    ILlmService llm) : ISearchService
{
    public async Task<object> SearchAsync(Guid tenantId, Guid actorId, SearchRequest req, CancellationToken ct = default)
    {
        var docQuery = new DocumentQuery(req.Query, WorkspaceId: req.Filters?.WorkspaceId, Page: req.Page, PageSize: req.PageSize);
        var docs = await documentRepo.SearchAsync(docQuery, ct);
        var items = await knowledgeRepo.SearchAsync(req.Query, null, Domain.Knowledge.KnowledgeItemStatus.Published, null, req.Page, req.PageSize, ct);

        var results = new List<dynamic>();

        results.AddRange(docs.Items.Select(d => new {
            Id = d.Id, Type = "document", Title = d.Title, Summary = $"Document • {d.MimeType}", CreatedAt = d.CreatedAt, Score = 0.8f }));

        results.AddRange(items.Items.Select(k => new {
            Id = k.Id, Type = "knowledge", Title = k.Title, Summary = $"Connaissance • {k.Type}", CreatedAt = k.CreatedAt, Score = 0.75f }));

        var sortedResults = results.OrderByDescending(r => r.Score).Take(req.PageSize).ToList();
        var total = docs.TotalCount + items.TotalCount;

        return new { Items = sortedResults, TotalCount = total, Page = req.Page, PageSize = req.PageSize };
    }

    public async Task<object> SuggestAsync(Guid tenantId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Array.Empty<string>();

        var docs = await documentRepo.SearchAsync(new DocumentQuery(query, Page: 1, PageSize: 5), ct);
        var items = await knowledgeRepo.SearchAsync(query, null, null, null, 1, 5, ct);

        var suggestions = docs.Items.Select(d => d.Title)
            .Concat(items.Items.Select(k => k.Title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return suggestions;
    }

    public async Task<object> AskAsync(Guid tenantId, Guid actorId, AskRequest req, CancellationToken ct = default)
    {
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

        return new
        {
            Answer = result.Output,
            Sources = items.Items.Select(i => new { i.Id, i.Title, i.Type }),
            Metadata = new { result.PromptTokens, result.CompletionTokens, result.ModelUsed }
        };
    }

    public async Task<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(Guid actorId, CancellationToken ct = default)
    {
        return await savedSearchRepo.GetByUserAsync(actorId, ct);
    }

    public async Task<SavedSearch> SaveSearchAsync(Guid tenantId, Guid actorId, SaveSearchRequest req, CancellationToken ct = default)
    {
        var saved = SavedSearch.Create(tenantId, actorId, req.Name, req.Query, SearchType.Basic, req.Filters, isShared: req.IsShared);
        await savedSearchRepo.AddAsync(saved, ct);
        await savedSearchRepo.SaveAsync(ct);
        return saved;
    }

    public async Task DeleteSavedSearchAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var search = await savedSearchRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Recherche sauvegardée introuvable.");
        if (search.UserId != actorId)
            throw new KeyNotFoundException("Recherche sauvegardée introuvable pour cet utilisateur.");

        savedSearchRepo.SoftDelete(search);
        await savedSearchRepo.SaveAsync(ct);
    }
}
