using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion des uploads de ressources, incluant les liens S3/MinIO présignés et multipart.
/// Route : /api/v1/uploads
/// </summary>
[Route("api/v1/uploads")]
[Authorize]
public sealed class ResourceUploadsController(
    IDocumentRepository documentRepo,
    IDocumentVersionRepository versionRepo,
    IStorageService storage) : V1ApiController
{
    [HttpPost("direct")]
    [Authorize(Policy = "resource.write")]
    public async Task<IActionResult> UploadDirect(
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
            TenantId, file.FileName, ActorId.Value,
            classification: classification, folderId: folderId, workspaceId: workspaceId);

        var version = DocumentVersion.Create(TenantId, doc.Id, 1, result.StorageKey, file.FileName, file.ContentType, file.Length, ActorId.Value, "Première version");

        await documentRepo.AddAsync(doc, ct);
        await versionRepo.AddAsync(version, ct);
        await documentRepo.SaveAsync(ct);

        return Ok200(new { doc.Id, doc.Title, VersionId = version.Id });
    }

    [HttpPost("presigned-url")]
    [Authorize(Policy = "resource.write")]
    public IActionResult GeneratePresignedUrl([FromBody] PresignedUrlRequest req)
    {
        // TODO: Mettre en œuvre la logique de presigned URL avec IStorageService si disponible.
        // Exemple :
        // var url = storage.GeneratePresignedUrl(req.FileName, TimeSpan.FromMinutes(15));
        // return Ok200(new { Url = url });
        return Ok(new { message = "Génération de presigned URL non implémentée (mock)." });
    }
}

public record PresignedUrlRequest(string FileName, string ContentType, long SizeBytes);
