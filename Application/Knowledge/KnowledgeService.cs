using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public sealed class KnowledgeService(
    IKnowledgeItemRepository itemRepo,
    IKnowledgeChunkRepository chunkRepo,
    IKnowledgePackRepository packRepo,
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

    public async Task<AskResponse> AskAsync(string question, Guid? packId, CancellationToken ct = default)
    {
        // Recherche sémantique dans la base de connaissance (simulée via SearchAsync pour l'instant)
        var items = await itemRepo.SearchAsync(question, null, KnowledgeItemStatus.Published, packId, 1, 5, ct);
        var context = string.Join("\n\n---\n\n", items.Items.Select(i => $"### {i.Title}\n{i.Content}"));

        var systemPrompt = $"""
            Tu es EAIOS, un assistant IA intelligent. Réponds en français à la question de l'utilisateur 
            en te basant UNIQUEMENT sur le contexte fourni ci-dessous. 
            Si la réponse n'est pas dans le contexte, dis-le clairement.
            
            CONTEXTE:
            {(string.IsNullOrWhiteSpace(context) ? "Aucun document pertinent trouvé." : context)}
            """;

        var result = await llm.GenerateAsync(systemPrompt, question, null, ct);

        return new AskResponse(
            Answer: result.Output,
            Sources: items.Items.Select(i => new SourceRef(i.Id, i.Title, i.Type)).ToList(),
            PromptTokens: result.PromptTokens,
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
