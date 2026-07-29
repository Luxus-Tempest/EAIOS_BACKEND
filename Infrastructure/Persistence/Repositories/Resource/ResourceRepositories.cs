using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Document?> GetWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Document>> SearchAsync(DocumentSearchParams p, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetTrashedAsync(CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
    void Update(Document document);
    void SoftDelete(Document document);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed record DocumentSearchParams(
    string? Search = null,
    Guid? FolderId = null,
    Guid? WorkspaceId = null,
    Guid? DepartmentId = null,
    ResourceClassification? Classification = null,
    ResourceStatus? Status = null,
    IndexingStatus? IndexingStatus = null,
    string[]? Tags = null,
    string[]? MimeTypes = null,
    int Page = 1,
    int PageSize = 20);

public sealed class DocumentRepository(EaiosDbContext db) : RepositoryBase<Document>(db), IDocumentRepository
{
    public async Task<Document?> GetWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        await Set.Include(d => d.Versions)
            .Include(d => d.Shares)
            .Include(d => d.MetadataValues)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<PagedResult<Document>> SearchAsync(DocumentSearchParams p, CancellationToken ct = default)
    {
        var query = Set.AsQueryable();
        if (!string.IsNullOrWhiteSpace(p.Search))
            query = query.Where(d => d.Title.Contains(p.Search));
        if (p.FolderId.HasValue)      query = query.Where(d => d.FolderId == p.FolderId);
        if (p.WorkspaceId.HasValue)   query = query.Where(d => d.WorkspaceId == p.WorkspaceId);
        if (p.DepartmentId.HasValue)  query = query.Where(d => d.DepartmentId == p.DepartmentId);
        if (p.Classification.HasValue) query = query.Where(d => d.Classification == p.Classification);
        if (p.Status.HasValue)        query = query.Where(d => d.Status == p.Status);
        else                          query = query.Where(d => d.Status != ResourceStatus.Deleted);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.CreatedAt)
            .Skip((p.Page - 1) * p.PageSize).Take(p.PageSize).ToListAsync(ct);
        return new PagedResult<Document>(items, p.Page, p.PageSize, total);
    }

    public async Task<IReadOnlyList<Document>> GetTrashedAsync(CancellationToken ct = default) =>
        (await Set.IgnoreQueryFilters()
            .Where(d => d.Status == ResourceStatus.Trashed && !d.IsDeleted)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct)).AsReadOnly();
}

public interface IDocumentVersionRepository
{
    Task<DocumentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentVersion>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
    Task<DocumentVersion?> GetCurrentAsync(Guid documentId, CancellationToken ct = default);
    Task AddAsync(DocumentVersion version, CancellationToken ct = default);
    void Update(DocumentVersion version);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class DocumentVersionRepository(EaiosDbContext db) : RepositoryBase<DocumentVersion>(db), IDocumentVersionRepository
{
    public async Task<IReadOnlyList<DocumentVersion>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default) =>
        (await Set.Where(v => v.DocumentId == documentId).OrderByDescending(v => v.VersionNumber).ToListAsync(ct)).AsReadOnly();

    public async Task<DocumentVersion?> GetCurrentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(v => v.DocumentId == documentId && v.IsCurrent, ct);
}

public interface IFolderRepository
{
    Task<Folder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> GetChildrenAsync(Guid? parentId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default);
    Task AddAsync(Folder folder, CancellationToken ct = default);
    void Update(Folder folder);
    void SoftDelete(Folder folder);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class FolderRepository(EaiosDbContext db) : RepositoryBase<Folder>(db), IFolderRepository
{
    public async Task<IReadOnlyList<Folder>> GetChildrenAsync(Guid? parentId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default)
    {
        var query = Set.AsQueryable();
        query = query.Where(f => f.ParentId == parentId);
        if (workspaceId.HasValue) query = query.Where(f => f.WorkspaceId == workspaceId);
        if (departmentId.HasValue) query = query.Where(f => f.DepartmentId == departmentId);
        return (await query.OrderBy(f => f.Name).ToListAsync(ct)).AsReadOnly();
    }

    public async Task<IReadOnlyList<Folder>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default) =>
        (await Set.Where(f => f.Path.StartsWith(pathPrefix)).ToListAsync(ct)).AsReadOnly();
}
