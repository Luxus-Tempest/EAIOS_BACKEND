using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Application.Resource;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Accès aux documents partagés par lien public. Ces endpoints sont anonymes :
/// le jeton de partage tient lieu d'autorisation.
///
/// Le tenant n'étant pas résolu par le JWT ici, il est déduit du partage lui-même
/// avant toute lecture de document — sans cela les Global Query Filters
/// masqueraient la ressource.
/// Route : /api/v1/public
/// </summary>
[Route("api/v1/public")]
[AllowAnonymous]
[ApiController]
public sealed class PublicDocumentsController(
    EaiosDbContext db,
    ITenantContext tenantContext,
    IDocumentService documentService,
    IDocumentRepository documentRepo,
    ILogger<PublicDocumentsController> logger) : ControllerBase
{
    /// <summary>Métadonnées minimales du document derrière un lien public.</summary>
    [HttpGet("documents/{token}/info")]
    public async Task<IActionResult> GetInfo(string token, CancellationToken ct)
    {
        if (!await ResolveTenantFromTokenAsync(token, ct))
            return NotFound(Problem404("Lien de partage invalide ou expiré."));

        var share = await db.DocumentShares.FirstOrDefaultAsync(s => s.PublicLinkToken == token, ct);
        if (share is null || share.IsExpired)
            return NotFound(Problem404("Lien de partage invalide ou expiré."));

        var doc = await documentRepo.GetByIdAsync(share.DocumentId, ct);
        if (doc is null)
            return NotFound(Problem404("Document introuvable."));

        // Volontairement minimal : un lien public ne doit pas divulguer
        // la classification, le propriétaire ni l'arborescence interne.
        return Ok(new
        {
            doc.Title,
            doc.MimeType,
            doc.FileSizeBytes,
            doc.Extension,
            share.Permission,
            share.ExpiresAt
        });
    }

    /// <summary>Télécharge le document derrière un lien public.</summary>
    [HttpGet("documents/{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        if (!await ResolveTenantFromTokenAsync(token, ct))
            return NotFound(Problem404("Lien de partage invalide ou expiré."));

        try
        {
            var download = await documentService.DownloadByPublicLinkAsync(token, ct);
            logger.LogInformation("Téléchargement via lien public {Token}.", token);
            return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem404("Lien de partage invalide ou expiré."));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
            {
                Status   = StatusCodes.Status410Gone,
                Title    = "Gone",
                Detail   = ex.Message,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Retrouve l'organisation propriétaire du partage et positionne le contexte tenant.
    /// La recherche ignore les filtres globaux : c'est la seule lecture autorisée
    /// hors tenant, et elle ne porte que sur le jeton.
    /// </summary>
    private async Task<bool> ResolveTenantFromTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var owner = await db.DocumentShares
            .IgnoreQueryFilters()
            .Where(s => s.PublicLinkToken == token && !s.IsDeleted && s.IsPublicLink)
            .Select(s => new { s.OrganizationId })
            .FirstOrDefaultAsync(ct);

        if (owner is null) return false;

        tenantContext.SetTenant(owner.OrganizationId);
        return true;
    }

    private ProblemDetails Problem404(string detail) => new()
    {
        Status   = StatusCodes.Status404NotFound,
        Title    = "Not Found",
        Detail   = detail,
        Instance = HttpContext.Request.Path
    };
}
