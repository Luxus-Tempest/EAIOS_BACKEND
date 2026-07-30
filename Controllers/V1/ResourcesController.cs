using EAIOS.Api.Application.Resource;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Storage;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Ressources documentaires : CRUD, upload, versioning, partage, corbeille, holds légaux.
/// </summary>
[Route("api/v1/resources")]
public sealed class ResourcesController(
    IDocumentRepository        documentRepo,
    IDocumentVersionRepository versionRepo,
    IFolderRepository          folderRepo,
    IDocumentShareRepository   shareRepo,
    ILegalHoldRepository       holdRepo,
    IStorageService            storage,
    IPermissionService         permService) : V1ApiController
{
    // ── Dossiers ──────────────────────────────────────────────────────────────

    [HttpGet("folders")]
    public async Task<IActionResult> GetFolders(
        [FromQuery] Guid? parentId,
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? departmentId,
        CancellationToken ct)
    {
        var folders = await folderRepo.GetChildrenAsync(parentId, workspaceId, departmentId, ct);
        return Ok200(folders.Select(MapFolder).ToList());
    }

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var parent = req.ParentId.HasValue ? await folderRepo.GetByIdAsync(req.ParentId.Value, ct) : null;
        var folder = Folder.Create(TenantId, req.Name, req.ParentId, req.WorkspaceId, req.DepartmentId, ActorId.Value, parent?.Path, parent?.Depth ?? 0);

        await folderRepo.AddAsync(folder, ct);
        await folderRepo.SaveAsync(ct);

        return Created201("GetFolder", new { id = folder.Id }, MapFolder(folder));
    }

    [HttpGet("folders/{id:guid}", Name = "GetFolder")]
    public async Task<IActionResult> GetFolder(Guid id, CancellationToken ct)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct);
        return folder == null ? NotFound() : Ok200(MapFolder(folder));
    }

    [HttpDelete("folders/{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var folder = await folderRepo.GetByIdAsync(id, ct);
        if (folder == null) return NotFound();
        folderRepo.SoftDelete(folder);
        await folderRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Documents ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] Guid? folderId,
        [FromQuery] Guid? workspaceId,
        [FromQuery] ResourceStatus? status,
        [FromQuery] ResourceClassification? classification,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await documentRepo.SearchAsync(
            new DocumentQuery(q, folderId, workspaceId, Classification: classification, Status: status, Page: page, PageSize: pageSize), ct);
        return OkList(result.Items.Select(MapDocument).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    [HttpGet("{id:guid}", Name = "GetDocument")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var doc = await documentRepo.GetWithDetailsAsync(id, ct);
        return doc == null ? NotFound() : Ok200(MapDocument(doc));
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] Guid? folderId,
        [FromQuery] Guid? workspaceId,
        [FromQuery] ResourceClassification classification = ResourceClassification.Internal,
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        if (file.Length == 0) return BadRequest(new { code = "EMPTY_FILE" });

        await using var stream = file.OpenReadStream();
        var result = await storage.UploadAsync(stream, file.FileName, file.ContentType, TenantId.ToString(), ct);

        var doc = Document.Create(
            TenantId, file.FileName, file.ContentType, result.StorageKey,
            file.Length, folderId, workspaceId, ActorId.Value, classification);

        var version = DocumentVersion.Create(TenantId, doc.Id, 1, result.StorageKey, file.Length, ActorId.Value, result.Checksum, "Première version");

        await documentRepo.AddAsync(doc, ct);
        await versionRepo.AddAsync(version, ct);
        await documentRepo.SaveAsync(ct);

        return Created201("GetDocument", new { id = doc.Id }, MapDocument(doc));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentRequest req, CancellationToken ct)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();

        doc.UpdateMetadata(req.Title, req.Description, req.Classification);
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);

        return Ok200(MapDocument(doc));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();

        // Vérifier legal hold
        var holds = await holdRepo.GetActiveByDocumentAsync(id, ct);
        if (holds.Count > 0) return UnprocessableEntity("Ce document est sous hold légal et ne peut pas être supprimé.");

        doc.MoveToTrash();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);
        return NoContent204();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();
        doc.Restore();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);
        return Ok200(MapDocument(doc));
    }

    // ── Versions ──────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> GetVersions(Guid id, CancellationToken ct)
    {
        var versions = await versionRepo.GetByDocumentAsync(id, ct);
        return Ok200(versions.Select(MapVersion).ToList());
    }

    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> UploadNewVersion(Guid id, IFormFile file, [FromQuery] string? changeNote, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();

        var versions = await versionRepo.GetByDocumentAsync(id, ct);
        var nextVersion = versions.Count + 1;

        await using var stream = file.OpenReadStream();
        var result = await storage.UploadAsync(stream, file.FileName, file.ContentType, TenantId.ToString(), ct);

        var current = versions.FirstOrDefault(v => v.IsCurrent);
        if (current != null) { current.MarkNotCurrent(); versionRepo.Update(current); }

        var version = DocumentVersion.Create(TenantId, id, nextVersion, result.StorageKey, file.Length, ActorId.Value, result.Checksum, changeNote);
        await versionRepo.AddAsync(version, ct);
        await versionRepo.SaveAsync(ct);

        return Ok200(MapVersion(version));
    }

    // ── Partage ───────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/shares")]
    public async Task<IActionResult> CreateShare(Guid id, [FromBody] CreateShareRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();

        var share = DocumentShare.Create(TenantId, id, ActorId.Value, req.Type, req.ExpiresAt, req.PermissionLevel, req.Password);
        await shareRepo.AddAsync(share, ct);
        await shareRepo.SaveAsync(ct);

        return Ok200(MapShare(share));
    }

    [HttpDelete("{id:guid}/shares/{shareId:guid}")]
    public async Task<IActionResult> RevokeShare(Guid id, Guid shareId, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share == null || share.DocumentId != id) return NotFound();
        share.Revoke();
        shareRepo.Update(share);
        await shareRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Legal Holds ───────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/legal-holds")]
    public async Task<IActionResult> CreateLegalHold(Guid id, [FromBody] CreateLegalHoldRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var hold = LegalHold.Create(TenantId, id, req.CaseName, req.CaseReference, req.Reason, ActorId.Value);
        await holdRepo.AddAsync(hold, ct);
        await holdRepo.SaveAsync(ct);
        return Ok200(hold);
    }

    [HttpDelete("{id:guid}/legal-holds/{holdId:guid}")]
    public async Task<IActionResult> ReleaseLegalHold(Guid id, Guid holdId, [FromBody] ReleaseLegalHoldRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var hold = await holdRepo.GetByIdAsync(holdId, ct);
        if (hold == null || hold.DocumentId != id) return NotFound();
        hold.Release(ActorId.Value, req.ReleaseReason);
        holdRepo.Update(hold);
        await holdRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapDocument(Document d) => new
    {
        d.Id, d.Title, d.MimeType, d.Extension, d.FileSizeBytes, d.ResourceType, d.Classification, d.Status,
        d.IndexingStatus, d.FolderId, d.WorkspaceId, d.DepartmentId, d.OwnerId, d.Language, d.Description,
        d.StorageKey, d.CreatedAt, d.UpdatedAt
    };

    private static object MapFolder(Folder f) => new
    {
        f.Id, f.Name, f.Path, f.Depth, f.ParentId, f.WorkspaceId, f.DepartmentId, f.CreatedAt
    };

    private static object MapVersion(DocumentVersion v) => new
    {
        v.Id, v.DocumentId, v.VersionNumber, v.FileSizeBytes, v.Checksum, v.IsCurrent, v.ChangeNote, v.CreatedAt
    };

    private static object MapShare(DocumentShare s) => new
    {
        s.Id, s.DocumentId, s.Type, s.PermissionLevel, s.PublicLinkToken, s.ExpiresAt, s.IsActive, s.CreatedAt
    };
}
