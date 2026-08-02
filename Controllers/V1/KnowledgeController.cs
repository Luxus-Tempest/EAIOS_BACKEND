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
    EAIOS.Api.Application.Knowledge.IKnowledgeService knowledgeService,
    IKnowledgeItemRepository  itemRepo,
    IKnowledgePackRepository  packRepo) : V1ApiController
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
        var item = await knowledgeService.CreateItemAsync(TenantId, req.Title, req.Type, req.Content, req.SourceDocumentId, ActorId.Value, ct);
        return Created201("GetKnowledgeItem", new { id = item.Id }, MapItem(item));
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateKnowledgeItemRequest req, CancellationToken ct)
    {
        try
        {
            var item = await knowledgeService.UpdateItemAsync(id, req.Title, req.Content, req.Summary, req.Tags, req.Language, ct);
            return Ok200(MapItem(item));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("items/{id:guid}/publish")]
    public async Task<IActionResult> PublishItem(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var item = await knowledgeService.PublishItemAsync(id, ActorId.Value, ct);
            return Ok200(MapItem(item));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("items/{id:guid}/validate")]
    public async Task<IActionResult> ValidateItem(Guid id, [FromBody] ValidateKnowledgeItemRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var item = await knowledgeService.ValidateItemAsync(id, ActorId.Value, ct);
            return Ok200(MapItem(item));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        try
        {
            await knowledgeService.DeleteItemAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Packs ─────────────────────────────────────────────────────────────────

    [HttpGet("packs")]
    public async Task<IActionResult> ListPacks([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await packRepo.GetPagedAsync(page, pageSize, ct: ct);
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
        var pack = await knowledgeService.CreatePackAsync(TenantId, req.Name, req.Description, req.IsPublic, ActorId.Value, ct);
        return Created201("GetKnowledgePack", new { id = pack.Id }, MapPack(pack));
    }

    [HttpDelete("packs/{id:guid}")]
    public async Task<IActionResult> DeletePack(Guid id, CancellationToken ct)
    {
        try
        {
            await knowledgeService.DeletePackAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── RAG Ask ───────────────────────────────────────────────────────────────

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var response = await knowledgeService.AskAsync(req.Question, req.PackId, ct);
        return Ok200(response);
    }



    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapItem(KnowledgeItem i) => new
    {
        i.Id, i.Title, i.Content, i.Type, i.Source, i.Status, i.Language, i.PackId,
        i.Tags, ValidatedBy = i.VerifiedBy, ValidatedAt = i.VerifiedAt, i.CreatedAt, i.UpdatedAt
    };

    private static object MapPack(KnowledgePack p) => new
    {
        p.Id, p.Name, p.Description, p.IsPublic, p.Status, p.CreatedAt
    };
}
