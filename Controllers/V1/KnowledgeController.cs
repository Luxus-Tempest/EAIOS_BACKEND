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
    IKnowledgePackRepository  packRepo,
    EAIOS.Api.Infrastructure.Security.IPermissionService permissions,
    EAIOS.Api.Infrastructure.Audit.IAuditService audit) : V1ApiController
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
        if (!ActorId.HasValue) return Unauthorized();
        // Une fiche extraite d'un document au-dessus du plafond de la personne n'apparaît pas.
        var ceiling = CurrentUser.IsPlatformAdmin ? (EAIOS.Api.Domain.Resource.ResourceClassification?)null
            : await permissions.GetClassificationCeilingAsync(ActorId.Value, ct);
        var result = await itemRepo.SearchAsync(q, type, status, packId, page, pageSize, ct, ceiling);
        return OkList(result.Items.Select(MapItem).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("items/{id:guid}", Name = "GetKnowledgeItem")]
    public async Task<IActionResult> GetItem(Guid id, CancellationToken ct)
    {
        var item = await itemRepo.GetWithChunksAsync(id, ct);
        if (item == null) return NotFound();

        await audit.LogAsync(TenantId, "knowledge.viewed", "User", EAIOS.Api.Domain.Platform.AuditEventResult.Success,
            actorId: ActorId, actorEmail: CurrentUser.Email, resourceId: item.Id, resourceType: "KnowledgeItem",
            resourceName: item.Title, module: "Knowledge", ct: ct);
        return Ok200(MapItem(item));
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] CreateKnowledgeItemRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var item = await knowledgeService.CreateItemAsync(TenantId, req, ActorId.Value, ct);
        return Created201("GetKnowledgeItem", new { id = item.Id }, MapItem(item));
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateKnowledgeItemRequest req, CancellationToken ct)
    {
        try
        {
            var item = await knowledgeService.UpdateItemAsync(id, req, ct);
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
    public async Task<IActionResult> ListPacks(
        [FromQuery] KnowledgePackStatus? status,
        [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await packRepo.SearchAsync(q, status, page, pageSize, ct);
        return OkList(result.Items.Select(MapPack).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("packs/{id:guid}", Name = "GetKnowledgePack")]
    public async Task<IActionResult> GetPack(Guid id, CancellationToken ct)
    {
        var pack = await packRepo.GetByIdAsync(id, ct);
        return pack == null ? NotFound() : Ok200(MapPack(pack));
    }

    [HttpPost("packs")]
    public async Task<IActionResult> CreatePack([FromBody] CreatePackRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var pack = await knowledgeService.CreatePackAsync(TenantId, req, ActorId.Value, ct);
        return Created201("GetKnowledgePack", new { id = pack.Id }, MapPack(pack));
    }

    [HttpPut("packs/{id:guid}")]
    public async Task<IActionResult> UpdatePack(Guid id, [FromBody] UpdatePackRequest req, CancellationToken ct)
    {
        try
        {
            return Ok200(MapPack(await knowledgeService.UpdatePackAsync(id, req, ct)));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Un pack publié devient le périmètre citable des agents qui y sont abonnés.</summary>
    [HttpPost("packs/{id:guid}/publish")]
    public async Task<IActionResult> PublishPack(Guid id, CancellationToken ct)
    {
        try
        {
            var pack = await knowledgeService.PublishPackAsync(id, ct);
            await audit.LogAsync(TenantId, "knowledge.pack_published", "User", EAIOS.Api.Domain.Platform.AuditEventResult.Success,
                actorId: ActorId, actorEmail: CurrentUser.Email, resourceId: pack.Id, resourceType: "KnowledgePack",
                resourceName: pack.Name, module: "Knowledge", ct: ct);
            return Ok200(MapPack(pack));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("packs/{id:guid}/archive")]
    public async Task<IActionResult> ArchivePack(Guid id, CancellationToken ct)
    {
        try
        {
            var pack = await knowledgeService.ArchivePackAsync(id, ct);
            await audit.LogAsync(TenantId, "knowledge.pack_archived", "User", EAIOS.Api.Domain.Platform.AuditEventResult.Success,
                actorId: ActorId, actorEmail: CurrentUser.Email, resourceId: pack.Id, resourceType: "KnowledgePack",
                resourceName: pack.Name, module: "Knowledge", ct: ct);
            return Ok200(MapPack(pack));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
        i.Id, i.Title, i.Summary, i.Content, i.Type, i.Source, i.Status, i.Language, i.PackId,
        i.SourceDocumentId, i.SourceVersionId, i.WorkspaceId, i.DepartmentId,
        i.IsVerifiedByHuman, i.VerifiedBy, i.VerifiedAt, i.ConfidenceScore, i.PublishedAt,
        i.Tags, i.ViewCount,
        ChunkCount = i.Chunks.Count,
        PageCount  = i.Chunks.Count == 0 ? (int?)null : i.Chunks.Max(c => c.EndPage ?? c.StartPage),
        ValidatedBy = i.VerifiedBy, ValidatedAt = i.VerifiedAt, i.CreatedAt, i.UpdatedAt, i.CreatedBy
    };

    // Le contrat de lecture existait et n'était branché nulle part : la carte
    // affichait « — éléments · » faute de compteur et de langue.
    private static KnowledgePackDto MapPack(KnowledgePack p) => new(
        p.Id, p.Name, p.Description, p.Status, p.Tags, p.Language, p.IsPublic,
        p.ItemCount, p.LastExportedAt, p.OwnerId, p.CreatedAt, p.UpdatedAt);
}
