using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Application.Resource;

public interface IFolderService
{
    Task<Folder> CreateFolderAsync(Guid tenantId, string name, Guid ownerId, Guid? parentId,
        Guid? workspaceId, Guid? departmentId, CancellationToken ct = default);

    Task<Folder> UpdateFolderAsync(Guid id, string? name, string? color, string? iconCode, CancellationToken ct = default);

    /// <summary>Déplace un dossier et réécrit le chemin matérialisé de tout son sous-arbre.</summary>
    Task<Folder> MoveFolderAsync(Guid id, Guid? newParentId, CancellationToken ct = default);

    /// <summary>Chaîne des ancêtres, de la racine jusqu'au dossier inclus.</summary>
    Task<IReadOnlyList<Folder>> GetBreadcrumbAsync(Guid id, CancellationToken ct = default);

    /// <summary>Arborescence complète sous un dossier (ou depuis la racine).</summary>
    Task<IReadOnlyList<Folder>> GetTreeAsync(Guid? rootId, Guid? workspaceId, CancellationToken ct = default);

    Task DeleteFolderAsync(Guid id, bool recursive = false, CancellationToken ct = default);
}

public sealed class FolderService(
    IFolderRepository folderRepo,
    EaiosDbContext db,
    ILogger<FolderService> logger) : IFolderService
{
    private const int MaxDepth = 20;

    public async Task<Folder> CreateFolderAsync(
        Guid tenantId, string name, Guid ownerId, Guid? parentId,
        Guid? workspaceId, Guid? departmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Le nom du dossier est obligatoire.");

        Folder? parent = null;
        if (parentId.HasValue)
        {
            parent = await folderRepo.GetByIdAsync(parentId.Value, ct)
                ?? throw new KeyNotFoundException("Dossier parent introuvable.");

            if (parent.Depth >= MaxDepth)
                throw new InvalidOperationException($"Profondeur maximale d'arborescence atteinte ({MaxDepth} niveaux).");

            // Hériter du contexte du parent si l'appelant ne l'a pas précisé.
            workspaceId  ??= parent.WorkspaceId;
            departmentId ??= parent.DepartmentId;
        }

        // Deux dossiers de même nom sous le même parent rendraient l'arborescence ambiguë.
        var siblings = await folderRepo.GetChildrenAsync(parentId, workspaceId, departmentId, ct);
        if (siblings.Any(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Un dossier nommé « {name.Trim()} » existe déjà à cet emplacement.");

        var folder = Folder.Create(
            tenantId, name, ownerId, parentId,
            parent?.Path ?? "/", parent?.Depth ?? 0,
            workspaceId, departmentId);

        await folderRepo.AddAsync(folder, ct);
        await folderRepo.SaveAsync(ct);
        return folder;
    }

    public async Task<Folder> UpdateFolderAsync(
        Guid id, string? name, string? color, string? iconCode, CancellationToken ct = default)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Dossier introuvable.");

        if (folder.IsSystemFolder && !string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Un dossier système ne peut pas être renommé.");

        if (!string.IsNullOrWhiteSpace(name) &&
            !string.Equals(folder.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var siblings = await folderRepo.GetChildrenAsync(folder.ParentId, folder.WorkspaceId, folder.DepartmentId, ct);
            if (siblings.Any(f => f.Id != id && string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Un dossier nommé « {name.Trim()} » existe déjà à cet emplacement.");

            folder.Rename(name);
        }

        folder.UpdateAppearance(color, iconCode);

        folderRepo.Update(folder);
        await folderRepo.SaveAsync(ct);
        return folder;
    }

    /// <summary>
    /// Déplace un dossier sous un nouveau parent. Le chemin matérialisé de chaque
    /// descendant est recalculé : sans cela, l'arborescence resterait incohérente
    /// et les requêtes par préfixe de chemin renverraient de mauvais résultats.
    /// </summary>
    public async Task<Folder> MoveFolderAsync(Guid id, Guid? newParentId, CancellationToken ct = default)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Dossier introuvable.");

        if (folder.IsSystemFolder)
            throw new InvalidOperationException("Un dossier système ne peut pas être déplacé.");

        if (newParentId == id)
            throw new InvalidOperationException("Un dossier ne peut pas être son propre parent.");

        Folder? newParent = null;
        if (newParentId.HasValue)
        {
            newParent = await folderRepo.GetByIdAsync(newParentId.Value, ct)
                ?? throw new KeyNotFoundException("Dossier de destination introuvable.");

            // Déplacer un dossier dans son propre sous-arbre détacherait la branche du graphe.
            if (newParent.Path.Contains($"/{id}/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Impossible de déplacer un dossier dans l'un de ses propres sous-dossiers.");
        }

        var oldPath  = folder.Path;
        var newDepth = (newParent?.Depth ?? 0) + 1;
        var newPath  = $"{newParent?.Path ?? "/"}{folder.Id}/";

        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return folder;   // déjà au bon endroit

        // Charger le sous-arbre AVANT de modifier le chemin du dossier déplacé.
        var descendants = await folderRepo.GetByPathPrefixAsync(oldPath, ct);
        var subtree     = descendants.Where(f => f.Id != folder.Id).ToList();

        var deepest = subtree.Count == 0 ? folder.Depth : subtree.Max(f => f.Depth);
        var shift   = newDepth - folder.Depth;
        if (deepest + shift > MaxDepth)
            throw new InvalidOperationException($"Ce déplacement dépasserait la profondeur maximale ({MaxDepth} niveaux).");

        folder.Move(newParentId, newPath, newDepth);
        folderRepo.Update(folder);

        foreach (var child in subtree)
        {
            // Remplacer uniquement le préfixe : le suffixe encode la position
            // relative du descendant, qui ne change pas lors du déplacement.
            var rewritten = string.Concat(newPath, child.Path.AsSpan(oldPath.Length));
            child.Move(child.ParentId, rewritten, child.Depth + shift);
            folderRepo.Update(child);
        }

        await folderRepo.SaveAsync(ct);

        logger.LogInformation("Dossier {FolderId} déplacé — {Count} descendant(s) réindexé(s).", id, subtree.Count);
        return folder;
    }

    public async Task<IReadOnlyList<Folder>> GetBreadcrumbAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Dossier introuvable.");

        var ancestors = await folderRepo.GetAncestorsAsync(id, ct);

        return ancestors
            .Where(a => a.Id != folder.Id)
            .OrderBy(a => a.Depth)
            .Append(folder)
            .ToList();
    }

    public async Task<IReadOnlyList<Folder>> GetTreeAsync(Guid? rootId, Guid? workspaceId, CancellationToken ct = default)
    {
        if (rootId.HasValue)
        {
            var root = await folderRepo.GetByIdAsync(rootId.Value, ct)
                ?? throw new KeyNotFoundException("Dossier racine introuvable.");

            var subtree = await folderRepo.GetByPathPrefixAsync(root.Path, ct);
            return subtree.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        }

        var query = db.Folders.Where(f => f.Status == FolderStatus.Active);
        if (workspaceId.HasValue)
            query = query.Where(f => f.WorkspaceId == workspaceId.Value);

        return await query.OrderBy(f => f.Path).ToListAsync(ct);
    }

    /// <summary>
    /// Supprime un dossier. Refuse par défaut si le dossier n'est pas vide :
    /// une suppression silencieuse laisserait des documents et sous-dossiers
    /// inaccessibles mais toujours présents.
    /// </summary>
    public async Task DeleteFolderAsync(Guid id, bool recursive = false, CancellationToken ct = default)
    {
        var folder = await folderRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Dossier introuvable.");

        if (folder.IsSystemFolder)
            throw new InvalidOperationException("Un dossier système ne peut pas être supprimé.");

        var subtree = (await folderRepo.GetByPathPrefixAsync(folder.Path, ct))
            .Where(f => f.Id != folder.Id)
            .ToList();

        var folderIds = subtree.Select(f => f.Id).Append(folder.Id).ToList();

        var documentCount = await db.Documents.CountAsync(d => d.FolderId != null && folderIds.Contains(d.FolderId.Value), ct);

        if (!recursive && (subtree.Count > 0 || documentCount > 0))
            throw new InvalidOperationException(
                $"Ce dossier n'est pas vide ({subtree.Count} sous-dossier(s), {documentCount} document(s)). " +
                "Utilisez recursive=true pour supprimer l'ensemble.");

        if (documentCount > 0)
        {
            // Les documents remontent à la racine plutôt que d'être supprimés :
            // la suppression d'un dossier ne doit jamais détruire du contenu.
            var documents = await db.Documents
                .Where(d => d.FolderId != null && folderIds.Contains(d.FolderId.Value))
                .ToListAsync(ct);

            foreach (var doc in documents)
                doc.MoveToFolder(null);

            logger.LogInformation("{Count} document(s) remonté(s) à la racine avant suppression du dossier {FolderId}.",
                documents.Count, id);
        }

        foreach (var child in subtree)
            folderRepo.SoftDelete(child);

        folderRepo.SoftDelete(folder);
        await folderRepo.SaveAsync(ct);

        logger.LogInformation("Dossier {FolderId} supprimé ({Count} sous-dossier(s)).", id, subtree.Count);
    }
}
