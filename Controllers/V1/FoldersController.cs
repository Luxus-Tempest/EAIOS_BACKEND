using EAIOS.Api.Application.Resource;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion des dossiers : arborescence, création, suppression.
/// Route : /api/v1/folders
/// </summary>
[Route("api/v1/folders")]
[Authorize]
public sealed class FoldersController(
    EAIOS.Api.Application.Resource.IFolderService folderService,
    IFolderRepository folderRepo) : V1ApiController
{
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

    [HttpPost]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var folder = await folderService.CreateFolderAsync(TenantId, req.Name, ActorId.Value, req.ParentId, req.WorkspaceId, req.DepartmentId, ct);
        return Created201("GetFolder", new { id = folder.Id }, MapFolder(folder));
    }

    [HttpGet("{id:guid}", Name = "GetFolder")]
    public async Task<IActionResult> GetFolder(Guid id, CancellationToken ct)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct);
        return folder == null ? NotFound() : Ok200(MapFolder(folder));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            await folderService.DeleteFolderAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static object MapFolder(Folder f) => new
    {
        f.Id, f.Name, f.Path, f.Depth, f.ParentId, f.WorkspaceId, f.DepartmentId, f.CreatedAt
    };
}
