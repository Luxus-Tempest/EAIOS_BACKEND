using EAIOS.Api.Application.Resource;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Storage;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Ressources documentaires : CRUD, upload, versioning, partage, corbeille, holds légaux.
/// </summary>
[Route("api/v1/documents")]
[Authorize]
public sealed class DocumentsController(
    EAIOS.Api.Application.Resource.IDocumentService documentService,
    IDocumentRepository        documentRepo,
    IDocumentVersionRepository versionRepo,
    IFolderRepository          folderRepo,
    IDocumentShareRepository   shareRepo,
    ILegalHoldRepository       holdRepo,
    IMetadataValueRepository   metadataRepo,
    IStorageService            storage,
    IPermissionService         permService) : V1ApiController
{
    // ── Documents (Metadonnées, versions, partages, holds) ────────────────────

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

    // Upload logic moved to ResourceUploadsController

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentRequest req, CancellationToken ct)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();

        doc.Update(req.Title, req.Description, req.Classification, req.Tags);
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);

        return Ok200(MapDocument(doc));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await documentService.DeleteDocumentAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "LEGAL_HOLD_ACTIVE")
        {
            return UnprocessableEntity("Ce document est sous hold légal et ne peut pas être supprimé.");
        }
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        try
        {
            var doc = await documentService.RestoreDocumentAsync(id, ct);
            return Ok200(MapDocument(doc));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
        if (current != null) { current.SetAsCurrent(false); versionRepo.Update(current); }

        var version = DocumentVersion.Create(TenantId, id, nextVersion, result.StorageKey, file.FileName, file.ContentType, file.Length, ActorId.Value, changeNote);
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

        var share = DocumentShare.CreateInternal(TenantId, id, req.TargetType, req.TargetId ?? Guid.Empty, req.Permission, ActorId.Value, req.ExpiresAt);
        await shareRepo.AddAsync(share, ct);
        await shareRepo.SaveAsync(ct);

        return Ok200(MapShare(share));
    }

    [HttpDelete("{id:guid}/shares/{shareId:guid}")]
    public async Task<IActionResult> RevokeShare(Guid id, Guid shareId, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share == null || share.DocumentId != id) return NotFound();
        shareRepo.SoftDelete(share);
        await shareRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Legal Holds ───────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/legal-holds")]
    public async Task<IActionResult> CreateLegalHold(Guid id, [FromBody] CreateLegalHoldRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var hold = await documentService.CreateLegalHoldAsync(TenantId, id, req.Reason, ActorId.Value, req.CaseReference, ct);
        return Ok200(hold);
    }

    [HttpDelete("{id:guid}/legal-holds/{holdId:guid}")]
    public async Task<IActionResult> ReleaseLegalHold(Guid id, Guid holdId, [FromBody] ReleaseLegalHoldRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            await documentService.ReleaseLegalHoldAsync(id, holdId, ActorId.Value, req.Reason, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException)
        {
            return BadRequest();
        }
    }

    // ── Corbeille ─────────────────────────────────────────────────────────────

    /// <summary>Documents mis à la corbeille, restaurables ou purgeables.</summary>
    [HttpGet("trash")]
    public async Task<IActionResult> GetTrash(CancellationToken ct)
    {
        var trashed = await documentRepo.GetTrashedAsync(ct);
        return Ok200(trashed.Select(MapDocument).ToList());
    }

    /// <summary>Suppression definitive, fichiers du stockage compris.</summary>
    [HttpDelete("{id:guid}/purge")]
    public async Task<IActionResult> Purge(Guid id, CancellationToken ct)
    {
        try
        {
            await documentService.PurgeDocumentAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Document introuvable.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "LEGAL_HOLD_ACTIVE")
        {
            return UnprocessableEntity("Ce document est sous hold legal et ne peut pas etre purge.");
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    // ── Deplacement ───────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/move")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveDocumentRequest req, CancellationToken ct)
    {
        try
        {
            var doc = await documentService.MoveDocumentAsync(id, req.FolderId, ct);
            return Ok200(MapDocument(doc));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Document introuvable.");
        }
    }

    // ── Telechargement ────────────────────────────────────────────────────────

    /// <summary>Telecharge la version courante, ou une version precise via ?versionId=.</summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] Guid? versionId, CancellationToken ct)
    {
        try
        {
            var download = await documentService.DownloadAsync(id, versionId, ct);
            return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    [HttpGet("{id:guid}/versions/{versionId:guid}/download")]
    public async Task<IActionResult> DownloadVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        try
        {
            var download = await documentService.DownloadAsync(id, versionId, ct);
            return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    /// <summary>Repromeut une version anterieure comme version courante.</summary>
    [HttpPost("{id:guid}/versions/{versionId:guid}/restore")]
    public async Task<IActionResult> RestoreVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var version = await documentService.RestoreVersionAsync(id, versionId, ActorId.Value, ct);
            return Ok200(MapVersion(version));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    // ── Metadonnees ───────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/metadata")]
    public async Task<IActionResult> GetMetadata(Guid id, CancellationToken ct)
    {
        try
        {
            var values = await documentService.GetMetadataAsync(id, ct);
            return Ok200(values.Select(MapMetadata).ToList());
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Document introuvable.");
        }
    }

    [HttpPut("{id:guid}/metadata")]
    public async Task<IActionResult> SetMetadata(Guid id, [FromBody] UpdateMetadataRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var values = await documentService.SetMetadataAsync(TenantId, id, req.Values, ActorId.Value, ct);
            return Ok200(values.Select(MapMetadata).ToList());
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Document introuvable.");
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    // ── Partages ──────────────────────────────────────────────────────────────

    /// <summary>Liste les partages actifs d'un document.</summary>
    [HttpGet("{id:guid}/shares")]
    public async Task<IActionResult> GetShares(Guid id, CancellationToken ct)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound("Document introuvable.");

        var shares = await shareRepo.GetByDocumentAsync(id, ct);
        return Ok200(shares.Select(MapShare).ToList());
    }

    /// <summary>Cree un lien de partage public, accessible sans authentification.</summary>
    [HttpPost("{id:guid}/public-link")]
    public async Task<IActionResult> CreatePublicLink(Guid id, [FromBody] CreatePublicLinkRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var share = await documentService.CreatePublicLinkAsync(
                TenantId, id, req.Permission, ActorId.Value, req.ExpiresAt, ct);

            return Ok200(new PublicLinkResult(
                share.Id,
                share.PublicLinkToken!,
                $"/api/v1/public/documents/{share.PublicLinkToken}",
                share.ExpiresAt));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Document introuvable.");
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    // ── Legal Holds (lecture) ─────────────────────────────────────────────────

    [HttpGet("{id:guid}/legal-holds")]
    public async Task<IActionResult> GetLegalHolds(Guid id, CancellationToken ct)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound("Document introuvable.");

        var holds = await holdRepo.GetActiveByDocumentAsync(id, ct);
        return Ok200(holds.Select(h => new LegalHoldDto(
            h.Id, h.DocumentId, h.Reason, h.CaseReference, h.Status,
            h.PlacedBy, h.PlacedAt, h.ReleasedAt, h.ReleasedBy, h.ReleaseReason)).ToList());
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapDocument(Document d) => new
    {
        d.Id, d.Title, d.MimeType, d.Extension, d.FileSizeBytes, d.ResourceType, d.Classification, d.Status,
        d.IndexingStatus, d.FolderId, d.WorkspaceId, d.DepartmentId, d.OwnerId, d.Language, d.Description,
        d.CreatedAt, d.UpdatedAt
    };

    // Removed MapFolder

    private static object MapVersion(DocumentVersion v) => new
    {
        v.Id, v.DocumentId, v.VersionNumber, v.FileSizeBytes, v.Checksum, v.IsCurrent, v.ChangeNote, v.CreatedAt
    };

    private static MetadataValueDto MapMetadata(MetadataValue m) =>
        new(m.FieldKey, m.FieldType, m.Value);

    private static object MapShare(DocumentShare s) => new
    {
        s.Id, s.DocumentId, s.TargetType, s.Permission, s.PublicLinkToken, s.ExpiresAt, IsActive = !s.IsExpired, s.CreatedAt
    };
}
