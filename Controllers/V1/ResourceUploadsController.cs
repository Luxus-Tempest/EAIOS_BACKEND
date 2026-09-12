using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Uploads de ressources : dépôt direct, liens présignés S3/MinIO et upload
/// multipart pour les fichiers volumineux.
/// Route : /api/v1/uploads
/// </summary>
[Route("api/v1/uploads")]
[Authorize]
public sealed class ResourceUploadsController(
    IDocumentRepository documentRepo,
    IDocumentVersionRepository versionRepo,
    IFolderRepository folderRepo,
    IStorageService storage,
    IAnalyticsTracker analytics,
    IConfiguration configuration,
    ILogger<ResourceUploadsController> logger,
    EAIOS.Api.Application.Realtime.IRealtimeEventService realtime) : V1ApiController
{
    // ── Dépôt direct ──────────────────────────────────────────────────────────

    [HttpPost("direct")]
    [Authorize(Policy = "resource.create")]
    [RequestSizeLimit(long.MaxValue)]   // la limite réelle est appliquée depuis la configuration
    public async Task<IActionResult> UploadDirect(
        IFormFile file,
        [FromQuery] Guid? folderId,
        [FromQuery] Guid? workspaceId,
        [FromQuery] ResourceClassification classification = ResourceClassification.Internal,
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new { code = "EMPTY_FILE", message = "Aucun fichier reçu." });

        var rejection = ValidateFile(file.FileName, file.Length);
        if (rejection is not null) return rejection;

        Folder? folder = null;
        if (folderId.HasValue)
        {
            folder = await folderRepo.GetByIdAsync(folderId.Value, ct);
            if (folder is null) return NotFound("Dossier de destination introuvable.");
            workspaceId ??= folder.WorkspaceId;
        }

        await using var stream = file.OpenReadStream();
        var result = await storage.UploadAsync(stream, file.FileName, file.ContentType, TenantId.ToString(), ct);

        var doc = Document.Create(
            TenantId, file.FileName, ActorId.Value,
            classification: classification, folderId: folderId, workspaceId: workspaceId);

        var version = DocumentVersion.Create(
            TenantId, doc.Id, 1, result.StorageKey, file.FileName, file.ContentType,
            result.FileSizeBytes, ActorId.Value, "Première version");

        // Sans cet appel, le document resterait sans version courante, sans type MIME
        // et avec une taille nulle : le téléchargement et l'affichage seraient cassés.
        doc.SetCurrentVersion(version.Id, file.ContentType, result.FileSizeBytes,
            Path.GetExtension(file.FileName).ToLowerInvariant(), null);

        await documentRepo.AddAsync(doc, ct);
        await versionRepo.AddAsync(version, ct);
        await documentRepo.SaveAsync(ct);
        await versionRepo.SaveAsync(ct);

        if (folder is not null)
        {
            folder.IncrementDocumentCount();
            folderRepo.Update(folder);
            await folderRepo.SaveAsync(ct);
        }

        await analytics.TrackAsync(AnalyticsEventTypes.DocumentUploaded,
            resourceId: doc.Id, resourceType: "Document",
            properties: new { fileName = file.FileName, sizeBytes = result.FileSizeBytes, mimeType = file.ContentType },
            workspaceId: workspaceId, ct: ct);

        logger.LogInformation("Document {DocumentId} déposé ({Size} octets).", doc.Id, result.FileSizeBytes);

        // Les listes ouvertes ailleurs se rafraîchissent ; l'extraction démarre sans attendre le balayage.
        await realtime.PublishToTenantAsync(TenantId, "document.uploaded", new { documentId = doc.Id, doc.Title });
        EAIOS.Api.Infrastructure.BackgroundJobs.DocumentIngestionWorker.Wakeup.Release();

        return Ok200(new
        {
            doc.Id,
            doc.Title,
            VersionId = version.Id,
            result.StorageKey,
            result.FileSizeBytes,
            result.Checksum
        });
    }

    // ── Lien présigné ─────────────────────────────────────────────────────────

    [HttpPost("presigned-url")]
    [Authorize(Policy = "resource.create")]
    public async Task<IActionResult> GeneratePresignedUrl([FromBody] PresignedUrlRequest req, CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var rejection = ValidateFile(req.FileName, req.SizeBytes);
        if (rejection is not null) return rejection;

        var url = await storage.GetPresignedUploadUrlAsync(
            req.FileName, req.ContentType, TenantId.ToString(), TimeSpan.FromMinutes(15), ct);

        return Ok200(new { Url = url, ExpiresInMinutes = 15 });
    }

    // ── Upload multipart ──────────────────────────────────────────────────────
    // Le stockage exposait déjà ces primitives sans qu'aucun endpoint ne les expose,
    // rendant impossible le dépôt de fichiers volumineux.

    /// <summary>Ouvre une session multipart et renvoie la taille de fragment attendue.</summary>
    [HttpPost("multipart/initiate")]
    [Authorize(Policy = "resource.create")]
    public async Task<IActionResult> InitiateMultipart([FromBody] InitiateMultipartRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var maxMultipart = configuration.GetValue("Storage:MaxMultipartFileSizeBytes", 5L * 1024 * 1024 * 1024);
        if (req.TotalSizeBytes <= 0)
            return UnprocessableEntity("La taille totale du fichier doit être renseignée.");
        if (req.TotalSizeBytes > maxMultipart)
            return UnprocessableEntity($"Fichier trop volumineux (maximum {maxMultipart / 1024 / 1024 / 1024} Go).");

        var extensionError = ValidateExtension(req.FileName);
        if (extensionError is not null) return UnprocessableEntity(extensionError);

        var session = await storage.InitiateMultipartAsync(
            req.FileName, req.TotalSizeBytes, req.ContentType, TenantId.ToString(), ct);

        return Ok200(new
        {
            session.UploadId,
            session.StorageKey,
            session.ChunkSizeBytes,
            session.ExpiresAt,
            ExpectedParts = (int)Math.Ceiling((double)req.TotalSizeBytes / session.ChunkSizeBytes)
        });
    }

    /// <summary>Dépose un fragment. Les fragments sont numérotés à partir de 1.</summary>
    [HttpPut("multipart/{uploadId}/parts/{partNumber:int}")]
    [Authorize(Policy = "resource.create")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> UploadPart(
        string uploadId, int partNumber, IFormFile file, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        if (partNumber < 1)
            return UnprocessableEntity("Le numéro de fragment commence à 1.");

        if (file is null || file.Length == 0)
            return BadRequest(new { code = "EMPTY_PART", message = "Fragment vide." });

        try
        {
            await using var stream = file.OpenReadStream();
            await storage.UploadPartAsync(uploadId, partNumber, stream, ct);

            return Ok200(new { uploadId, partNumber, SizeBytes = file.Length });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    /// <summary>Finalise la session et crée le document correspondant.</summary>
    [HttpPost("multipart/{uploadId}/complete")]
    [Authorize(Policy = "resource.create")]
    public async Task<IActionResult> CompleteMultipart(
        string uploadId, [FromBody] CompleteMultipartRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var result = await storage.CompleteMultipartAsync(uploadId, TenantId.ToString(), ct);

            Folder? folder = null;
            var workspaceId = req.WorkspaceId;
            if (req.FolderId.HasValue)
            {
                folder = await folderRepo.GetByIdAsync(req.FolderId.Value, ct);
                if (folder is null) return NotFound("Dossier de destination introuvable.");
                workspaceId ??= folder.WorkspaceId;
            }

            var title = string.IsNullOrWhiteSpace(req.Title) ? result.OriginalFileName : req.Title!;

            var doc = Document.Create(
                TenantId, title, ActorId.Value,
                classification: req.Classification, folderId: req.FolderId, workspaceId: workspaceId);

            var version = DocumentVersion.Create(
                TenantId, doc.Id, 1, result.StorageKey, result.OriginalFileName, result.MimeType,
                result.FileSizeBytes, ActorId.Value, "Première version (upload multipart)");

            doc.SetCurrentVersion(version.Id, result.MimeType, result.FileSizeBytes,
                Path.GetExtension(result.OriginalFileName).ToLowerInvariant(), null);

            await documentRepo.AddAsync(doc, ct);
            await versionRepo.AddAsync(version, ct);
            await documentRepo.SaveAsync(ct);
            await versionRepo.SaveAsync(ct);

            if (folder is not null)
            {
                folder.IncrementDocumentCount();
                folderRepo.Update(folder);
                await folderRepo.SaveAsync(ct);
            }

            await analytics.TrackAsync(AnalyticsEventTypes.DocumentUploaded,
                resourceId: doc.Id, resourceType: "Document",
                properties: new { fileName = result.OriginalFileName, sizeBytes = result.FileSizeBytes, multipart = true },
                workspaceId: workspaceId, ct: ct);

            logger.LogInformation("Upload multipart {UploadId} finalisé — document {DocumentId}.", uploadId, doc.Id);

            return Ok200(new
            {
                doc.Id,
                doc.Title,
                VersionId = version.Id,
                result.StorageKey,
                result.FileSizeBytes,
                result.Checksum
            });
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

    /// <summary>Abandonne une session multipart et libère les fragments déjà déposés.</summary>
    [HttpDelete("multipart/{uploadId}")]
    [Authorize(Policy = "resource.create")]
    public async Task<IActionResult> AbortMultipart(string uploadId, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            await storage.AbortMultipartAsync(uploadId, ct);
            return NoContent204();
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>Applique les limites déclarées en configuration. Renvoie null si le fichier est acceptable.</summary>
    private IActionResult? ValidateFile(string fileName, long sizeBytes)
    {
        var maxSize = configuration.GetValue("Storage:MaxFileSizeBytes", 10L * 1024 * 1024);
        if (sizeBytes > maxSize)
            return UnprocessableEntity(
                $"Fichier trop volumineux ({sizeBytes / 1024 / 1024} Mo). Maximum autorisé : {maxSize / 1024 / 1024} Mo. " +
                "Utilisez l'upload multipart pour les fichiers plus lourds.");

        var extensionError = ValidateExtension(fileName);
        return extensionError is null ? null : UnprocessableEntity(extensionError);
    }

    private string? ValidateExtension(string fileName)
    {
        var allowed = configuration.GetSection("Storage:AllowedExtensions").Get<string[]>();
        if (allowed is null || allowed.Length == 0) return null;   // aucune restriction configurée

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
            return "Le fichier doit porter une extension.";

        return allowed.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"Extension « {extension} » non autorisée. Extensions acceptées : {string.Join(", ", allowed)}.";
    }
}

// ── Modèles de requête ────────────────────────────────────────────────────────

public sealed record PresignedUrlRequest(string FileName, string ContentType, long SizeBytes);

public sealed record InitiateMultipartRequest(
    string FileName,
    string ContentType,
    long TotalSizeBytes);

public sealed record CompleteMultipartRequest(
    string? Title = null,
    Guid? FolderId = null,
    Guid? WorkspaceId = null,
    ResourceClassification Classification = ResourceClassification.Internal);
