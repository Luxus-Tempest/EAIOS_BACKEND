using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;

namespace EAIOS.Api.Application.Resource;

public interface IFolderService
{
    Task<Folder> CreateFolderAsync(Guid tenantId, string name, Guid ownerId, Guid? parentId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default);
    Task DeleteFolderAsync(Guid id, CancellationToken ct = default);
}

public sealed class FolderService(IFolderRepository folderRepo) : IFolderService
{
    public async Task<Folder> CreateFolderAsync(Guid tenantId, string name, Guid ownerId, Guid? parentId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default)
    {
        var parent = parentId.HasValue ? await folderRepo.GetByIdAsync(parentId.Value, ct) : null;
        var folder = Folder.Create(tenantId, name, ownerId, parentId, parent?.Path ?? "/", parent?.Depth ?? 0, workspaceId, departmentId);

        await folderRepo.AddAsync(folder, ct);
        await folderRepo.SaveAsync(ct);
        return folder;
    }

    public async Task DeleteFolderAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Dossier introuvable.");
        
        // On pourrait vérifier si le dossier contient des enfants ou documents ici
        folderRepo.SoftDelete(folder);
        await folderRepo.SaveAsync(ct);
    }
}
