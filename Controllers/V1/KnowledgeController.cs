using EAIOS.Api.Application.Knowledge;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Base de connaissance : items, chunks, packs, RAG ask.
/// </summary>
[Route("api/v1/knowledge")]
public sealed class KnowledgeController(
    IKnowledgeItemRepository  itemRepo,
    IKnowledgeChunkRepository chunkRepo,
    IKnowledgePackRepository  packRepo,
    ILlmService               llm) : V1ApiController
{
    // ── Items ─────────────────────────────────────────────────────────────────

    [HttpGet("items")]
    public async Task<IActionResult> ListItems(
        [FromQuery] string? q,
        [FromQuery] KnowledgeItemType? type,
        [FromQuery] KnowledgeItemStatus? status,
        [FromQuery] Guid? packId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await itemRepo.SearchAsync(q, type, status, packId, page, pageSize, ct);
        return OkList(result.Items.Select(MapItem).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("items/{id:guid}", Name = "GetKnowledgeItem")]
    public async Task<IActionResult> GetItem(Guid id, CancellationToken ct)
    {
        var item = await itemRepo.GetWithChunksAsync(id, ct);
        return item == null ? NotFound() : Ok200(MapItem(item));
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] CreateKnowledgeItemRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var item = KnowledgeItem.Create(TenantId, req.Title, req.Content, req.Type, req.Source, req.Language ?? "fr", req.PackId, ActorId.Value);

        await itemRepo.AddAsync(item, ct);
        await itemRepo.SaveAsync(ct);

        // Créer les chunks automatiquement
        var chunks = ChunkText(item.Id, item.Content, TenantId);
        await chunkRepo.AddRangeAsync(chunks, ct);
        await chunkRepo.SaveAsync(ct);

        return Created201("GetKnowledgeItem", new { id = item.Id }, MapItem(item));
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateKnowledgeItemRequest req, CancellationToken ct)
    {
        var item = await itemRepo.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        item.Update(req.Title, req.Content, req.Tags, req.Language);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);
        return Ok200(MapItem(item));
    }

    [HttpPost("items/{id:guid}/publish")]
    public async Task<IActionResult> PublishItem(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var item = await itemRepo.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        item.Publish(ActorId.Value);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);
        return Ok200(MapItem(item));
    }

    [HttpPost("items/{id:guid}/validate")]
    public async Task<IActionResult> ValidateItem(Guid id, [FromBody] ValidateKnowledgeItemRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var item = await itemRepo.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        item.Validate(ActorId.Value, req.Note);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);
        return Ok200(MapItem(item));
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        var item = await itemRepo.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        itemRepo.SoftDelete(item);
        await itemRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Packs ─────────────────────────────────────────────────────────────────

    [HttpGet("packs")]
    public async Task<IActionResult> ListPacks([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await packRepo.GetPagedAsync(page, pageSize, ct);
        return OkList(result.Items.Select(MapPack).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("packs/{id:guid}", Name = "GetKnowledgePack")]
    public async Task<IActionResult> GetPack(Guid id, CancellationToken ct)
    {
        var pack = await packRepo.GetByIdAsync(id, ct);
        return pack == null ? NotFound() : Ok200(MapPack(pack));
    }

    [HttpPost("packs")]
    public async Task<IActionResult> CreatePack([FromBody] CreateKnowledgePackRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var pack = KnowledgePack.Create(TenantId, req.Name, req.Description, req.IsPublic, ActorId.Value);
        await packRepo.AddAsync(pack, ct);
        await packRepo.SaveAsync(ct);
        return Created201("GetKnowledgePack", new { id = pack.Id }, MapPack(pack));
    }

    [HttpDelete("packs/{id:guid}")]
    public async Task<IActionResult> DeletePack(Guid id, CancellationToken ct)
    {
        var pack = await packRepo.GetByIdAsync(id, ct);
        if (pack == null) return NotFound();
        packRepo.SoftDelete(pack);
        await packRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── RAG Ask ───────────────────────────────────────────────────────────────

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        // Recherche sémantique dans la base de connaissance
        var items = await itemRepo.SearchAsync(req.Question, null, KnowledgeItemStatus.Published, req.PackId, 1, 5, ct);
        var context = string.Join("\n\n---\n\n", items.Items.Select(i => $"### {i.Title}\n{i.Content}"));

        var systemPrompt = $"""
            Tu es EAIOS, un assistant IA intelligent. Réponds en français à la question de l'utilisateur 
            en te basant UNIQUEMENT sur le contexte fourni ci-dessous. 
            Si la réponse n'est pas dans le contexte, dis-le clairement.
            
            CONTEXTE:
            {(string.IsNullOrWhiteSpace(context) ? "Aucun document pertinent trouvé." : context)}
            """;

        var result = await llm.GenerateAsync(systemPrompt, req.Question, null, ct);

        return Ok200(new AskResponse(
            Answer:  result.Output,
            Sources: items.Items.Select(i => new SourceRef(i.Id, i.Title, i.Type)).ToList(),
            PromptTokens: result.PromptTokens,
            CompletionTokens: result.CompletionTokens));
    }

    // ── Chunking helper ───────────────────────────────────────────────────────

    private static List<KnowledgeChunk> ChunkText(Guid itemId, string? content, Guid orgId)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        const int chunkSize = 1000;
        const int overlap   = 100;
        var chunks = new List<KnowledgeChunk>();
        var i = 0;
        var idx = 0;
        while (i < content.Length)
        {
            var end  = Math.Min(i + chunkSize, content.Length);
            var text = content[i..end];
            chunks.Add(KnowledgeChunk.Create(orgId, itemId, idx++, text));
            i += chunkSize - overlap;
        }
        return chunks;
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapItem(KnowledgeItem i) => new
    {
        i.Id, i.Title, i.Content, i.Type, i.Source, i.Status, i.Language, i.PackId,
        i.Tags, i.ValidatedBy, i.ValidatedAt, i.CreatedAt, i.UpdatedAt
    };

    private static object MapPack(KnowledgePack p) => new
    {
        p.Id, p.Name, p.Description, p.IsPublic, p.Status, p.CreatedAt
    };
}
