using EAIOS.Api.Application.Resource;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion des dossiers : arborescence, création, renommage, déplacement, suppression.
/// Route : /api/v1/folders
/// </summary>
[Route("api/v1/folders")]
[Authorize]
public sealed class FoldersController(
    IFolderService folderService,
    IFolderRepository folderRepo) : V1ApiController
{
    // ── Lecture ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetFolders(
        [FromQuery] Guid? parentId,
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? departmentId,
        CancellationToken ct)
    {
        var folders = await folderRepo.GetChildrenAsync(parentId, workspaceId, departmentId, ct);
        return Ok200(folders.Select(MapFolder).ToList());
    }

    [HttpGet("{id:guid}", Name = "GetFolder")]
    public async Task<IActionResult> GetFolder(Guid id, CancellationToken ct)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct);
        return folder == null ? NotFound("Dossier introuvable.") : Ok200(MapFolder(folder));
    }

    /// <summary>Arborescence complète sous un dossier, ou depuis la racine.</summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree(
        [FromQuery] Guid? rootId,
        [FromQuery] Guid? workspaceId,
        CancellationToken ct)
    {
        try
        {
            var tree = await folderService.GetTreeAsync(rootId, workspaceId, ct);
            return Ok200(tree.Select(MapFolder).ToList());
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Dossier racine introuvable.");
        }
    }

    /// <summary>Fil d'Ariane : de la racine jusqu'au dossier demandé.</summary>
    [HttpGet("{id:guid}/breadcrumb")]
    public async Task<IActionResult> GetBreadcrumb(Guid id, CancellationToken ct)
    {
        try
        {
            var trail = await folderService.GetBreadcrumbAsync(id, ct);
            return Ok200(trail.Select(f => new { f.Id, f.Name, f.Depth, f.ParentId }).ToList());
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Dossier introuvable.");
        }
    }

    // ── Écriture ──────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var folder = await folderService.CreateFolderAsync(
                TenantId, req.Name, ActorId.Value, req.ParentId, req.WorkspaceId, req.DepartmentId, ct);

            return Created201("GetFolder", new { id = folder.Id }, MapFolder(folder));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> UpdateFolder(Guid id, [FromBody] UpdateFolderRequest req, CancellationToken ct)
    {
        try
        {
            var folder = await folderService.UpdateFolderAsync(id, req.Name, req.Color, req.IconCode, ct);
            return Ok200(MapFolder(folder));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Dossier introuvable.");
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPost("{id:guid}/move")]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> MoveFolder(Guid id, [FromBody] MoveFolderRequest req, CancellationToken ct)
    {
        try
        {
            var folder = await folderService.MoveFolderAsync(id, req.NewParentId, ct);
            return Ok200(MapFolder(folder));
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

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> DeleteFolder(
        Guid id,
        [FromQuery] bool recursive = false,
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            await folderService.DeleteFolderAsync(id, recursive, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Dossier introuvable.");
        }
        catch (InvalidOperationException ex)
        {
            // Dossier non vide sans recursive=true, ou dossier système.
            return Conflict(ex.Message);
        }
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static FolderDto MapFolder(Folder f) => new(
        f.Id, f.Name, f.ParentId, f.Path, f.Depth,
        f.WorkspaceId, f.DepartmentId, f.Status, f.IsSystemFolder,
        f.Color, f.IconCode, f.DocumentCount, f.SizeBytes, f.CreatedAt);
}
