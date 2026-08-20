using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public sealed class KnowledgeService(
    IKnowledgeItemRepository itemRepo,
    IKnowledgeChunkRepository chunkRepo,
    IKnowledgePackRepository packRepo,
    IVectorSearchService vectorSearch,
    EAIOS.Api.Infrastructure.Analytics.IAnalyticsTracker analytics,
    ILlmService llm) : IKnowledgeService
{
    public async Task<KnowledgeItem> CreateItemAsync(Guid tenantId, string title, KnowledgeItemType type, string? content, Guid? sourceDocumentId, Guid actorId, CancellationToken ct = default)
    {
        var item = KnowledgeItem.Create(tenantId, title, type, KnowledgeItemSource.Manual, actorId, content, sourceDocumentId);

        await itemRepo.AddAsync(item, ct);
        await itemRepo.SaveAsync(ct);

        // Créer les chunks automatiquement
        var chunks = ChunkText(item.Id, item.Content, tenantId);
        if (chunks.Count > 0)
        {
            await chunkRepo.AddRangeAsync(chunks, ct);
            await chunkRepo.SaveAsync(ct);
        }

        return item;
    }

    public async Task<KnowledgeItem> UpdateItemAsync(Guid id, string? title, string? content, string? summary, string[]? tags, string? language, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");
        
        item.Update(title, content, summary, tags, language);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);
        
        return item;
    }

    public async Task<KnowledgeItem> PublishItemAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");
        
        item.Publish(actorId);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);
        
        return item;
    }

    public async Task<KnowledgeItem> ValidateItemAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");
        
        item.Validate(true, actorId);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);
        
        return item;
    }

    public async Task DeleteItemAsync(Guid id, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");
        
        itemRepo.SoftDelete(item);
        await itemRepo.SaveAsync(ct);
    }

    public async Task<KnowledgePack> CreatePackAsync(Guid tenantId, string name, string? description, bool isPublic, Guid actorId, CancellationToken ct = default)
    {
        var pack = KnowledgePack.Create(tenantId, name, actorId, description, isPublic);
        
        await packRepo.AddAsync(pack, ct);
        await packRepo.SaveAsync(ct);
        
        return pack;
    }

    public async Task DeletePackAsync(Guid id, CancellationToken ct = default)
    {
        var pack = await packRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Pack introuvable.");
        
        packRepo.SoftDelete(pack);
        await packRepo.SaveAsync(ct);
    }

    /// <summary>
    /// Question/reponse ancree (RAG). La recherche est d'abord semantique sur les
    /// embeddings des chunks ; en l'absence de vecteurs (chunks pas encore traites
    /// par le worker de vectorisation), on retombe sur la recherche lexicale afin
    /// que la fonctionnalite reste utilisable des le premier contenu cree.
    /// </summary>
    public async Task<AskResponse> AskAsync(string question, Guid? packId, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        string context;
        List<SourceRef> sources;
        string retrievalMode;

        var hits = await vectorSearch.SearchChunksAsync(question, topK: 6, minScore: 0.15f, packId: packId, ct: ct);

        if (hits.Count > 0)
        {
            retrievalMode = "semantic";

            context = string.Join("\n\n---\n\n",
                hits.Select(h => $"### {h.ItemTitle} (pertinence {h.Score:0.00})\n{h.Content}"));

            // Plusieurs chunks peuvent provenir du meme item : une seule source par item.
            var itemIds = hits.Select(h => h.ItemId).Distinct().ToList();
            sources = [];
            foreach (var itemId in itemIds)
            {
                var item = await itemRepo.GetByIdAsync(itemId, ct);
                if (item is not null) sources.Add(new SourceRef(item.Id, item.Title, item.Type));
            }
        }
        else
        {
            retrievalMode = "lexical";

            var items = await itemRepo.SearchAsync(question, null, KnowledgeItemStatus.Published, packId, 1, 5, ct);
            context = string.Join("\n\n---\n\n", items.Items.Select(i => $"### {i.Title}\n{i.Content}"));
            sources = items.Items.Select(i => new SourceRef(i.Id, i.Title, i.Type)).ToList();
        }

        var systemPrompt = $"""
            Tu es EAIOS, un assistant IA intelligent. Reponds en francais a la question de l'utilisateur
            en te basant UNIQUEMENT sur le contexte fourni ci-dessous.
            Si la reponse n'est pas dans le contexte, dis-le clairement.

            CONTEXTE:
            {(string.IsNullOrWhiteSpace(context) ? "Aucun document pertinent trouve." : context)}
            """;

        var result = await llm.GenerateAsync(systemPrompt, question, null, ct);
        stopwatch.Stop();

        await analytics.TrackAsync(
            EAIOS.Api.Infrastructure.Analytics.AnalyticsEventTypes.KnowledgeAsked,
            resourceType: "KnowledgeItem",
            durationMs:   stopwatch.ElapsedMilliseconds,
            properties: new
            {
                query       = question,
                resultCount = sources.Count,
                mode        = retrievalMode,
                tokens      = result.TotalTokens
            },
            ct: ct);

        return new AskResponse(
            Answer:           result.Output,
            Sources:          sources,
            PromptTokens:     result.PromptTokens,
            CompletionTokens: result.CompletionTokens);
    }

    // ── Chunking helper ───────────────────────────────────────────────────────

    private static List<KnowledgeChunk> ChunkText(Guid itemId, string? content, Guid orgId)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        const int chunkSize = 1000;
        const int overlap = 100;
        var chunks = new List<KnowledgeChunk>();
        var i = 0;
        var idx = 0;
        while (i < content.Length)
        {
            var end = Math.Min(i + chunkSize, content.Length);
            var text = content[i..end];
            chunks.Add(KnowledgeChunk.Create(orgId, itemId, idx++, text, text.Length / 4 + 1));
            i += chunkSize - overlap;
        }
        return chunks;
    }
}
