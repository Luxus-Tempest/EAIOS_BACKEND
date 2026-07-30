using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;

// ── IDocumentRepository ──────────────────────────────────────────────────────

public sealed record DocumentQuery(
    string?                Search         = null,
    Guid?                  FolderId       = null,
    Guid?                  WorkspaceId    = null,
    Guid?                  DepartmentId   = null,
    ResourceClassification? Classification = null,
    ResourceStatus?        Status         = null,
    IndexingStatus?        IndexingStatus = null,
    string[]?              Tags           = null,
    string[]?              MimeTypes      = null,
    DateTime?              DateFrom       = null,
    DateTime?              DateTo         = null,
    int                    Page           = 1,
    int                    PageSize       = 20);

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Document?> GetWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Document>> SearchAsync(DocumentQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetTrashedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
    void Update(Document document);
    void SoftDelete(Document document);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class DocumentRepository(EaiosDbContext db) : RepositoryBase<Document>(db), IDocumentRepository
{
    public async Task<Document?> GetWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        await Set.AsNoTracking()
                 .Include(d => d.Versions)
                 .Include(d => d.Shares)
                 .Include(d => d.MetadataValues)
                 .Include(d => d.LegalHolds)
                 .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<PagedResult<Document>> SearchAsync(DocumentQuery q, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q.Search))
            query = query.Where(d => d.Title.Contains(q.Search));
        if (q.FolderId.HasValue)       query = query.Where(d => d.FolderId     == q.FolderId);
        if (q.WorkspaceId.HasValue)    query = query.Where(d => d.WorkspaceId  == q.WorkspaceId);
        if (q.DepartmentId.HasValue)   query = query.Where(d => d.DepartmentId == q.DepartmentId);
        if (q.Classification.HasValue) query = query.Where(d => d.Classification == q.Classification);
        if (q.Status.HasValue)         query = query.Where(d => d.Status == q.Status);
        else                           query = query.Where(d => d.Status != ResourceStatus.Deleted);
        if (q.DateFrom.HasValue)       query = query.Where(d => d.CreatedAt >= q.DateFrom);
        if (q.DateTo.HasValue)         query = query.Where(d => d.CreatedAt <= q.DateTo);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.UpdatedAt)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);

        return new PagedResult<Document>(items, q.Page, q.PageSize, total);
    }

    public async Task<IReadOnlyList<Document>> GetTrashedAsync(CancellationToken ct = default) =>
        await Set.IgnoreQueryFilters()
                 .Where(d => d.Status == ResourceStatus.Trashed && d.OrganizationId == db.Database.CurrentTransaction != null ? Guid.Empty : Guid.Empty)
                 .OrderByDescending(d => d.UpdatedAt)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<Document>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default) =>
        await Set.Where(d => d.OwnerId == ownerId).OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
}

// ── IDocumentVersionRepository ───────────────────────────────────────────────

public interface IDocumentVersionRepository
{
    Task<DocumentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentVersion>> GetByDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<DocumentVersion?> GetCurrentAsync(Guid documentId, CancellationToken ct = default);
    Task AddAsync(DocumentVersion version, CancellationToken ct = default);
    void Update(DocumentVersion version);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class DocumentVersionRepository(EaiosDbContext db) : RepositoryBase<DocumentVersion>(db), IDocumentVersionRepository
{
    public async Task<IReadOnlyList<DocumentVersion>> GetByDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.Where(v => v.DocumentId == documentId)
                 .OrderByDescending(v => v.VersionNumber)
                 .ToListAsync(ct);

    public async Task<DocumentVersion?> GetCurrentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(v => v.DocumentId == documentId && v.IsCurrent, ct);
}

// ── IFolderRepository ────────────────────────────────────────────────────────

public interface IFolderRepository
{
    Task<Folder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> GetChildrenAsync(Guid? parentId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> GetAncestorsAsync(Guid folderId, CancellationToken ct = default);
    Task AddAsync(Folder folder, CancellationToken ct = default);
    void Update(Folder folder);
    void SoftDelete(Folder folder);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class FolderRepository(EaiosDbContext db) : RepositoryBase<Folder>(db), IFolderRepository
{
    public async Task<IReadOnlyList<Folder>> GetChildrenAsync(Guid? parentId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().Where(f => f.ParentId == parentId);
        if (workspaceId.HasValue)  q = q.Where(f => f.WorkspaceId  == workspaceId);
        if (departmentId.HasValue) q = q.Where(f => f.DepartmentId == departmentId);
        return await q.OrderBy(f => f.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Folder>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default) =>
        await Set.Where(f => f.Path.StartsWith(pathPrefix)).OrderBy(f => f.Depth).ThenBy(f => f.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Folder>> GetAncestorsAsync(Guid folderId, CancellationToken ct = default)
    {
        var folder = await Set.FirstOrDefaultAsync(f => f.Id == folderId, ct);
        if (folder == null) return [];

        var segments = folder.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ids = segments.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        return await Set.Where(f => ids.Contains(f.Id)).OrderBy(f => f.Depth).ToListAsync(ct);
    }
}

// ── IDocumentShareRepository ─────────────────────────────────────────────────

public interface IDocumentShareRepository
{
    Task<DocumentShare?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DocumentShare?> FindByTokenAsync(string token, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentShare>> GetByDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task AddAsync(DocumentShare share, CancellationToken ct = default);
    void Update(DocumentShare share);
    void SoftDelete(DocumentShare share);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class DocumentShareRepository(EaiosDbContext db) : RepositoryBase<DocumentShare>(db), IDocumentShareRepository
{
    public async Task<DocumentShare?> FindByTokenAsync(string token, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(s => s.PublicLinkToken == token, ct);

    public async Task<IReadOnlyList<DocumentShare>> GetByDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.Where(s => s.DocumentId == documentId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
}

// ── ILegalHoldRepository ─────────────────────────────────────────────────────

public interface ILegalHoldRepository
{
    Task<LegalHold?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LegalHold>> GetActiveByDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task AddAsync(LegalHold hold, CancellationToken ct = default);
    void Update(LegalHold hold);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class LegalHoldRepository(EaiosDbContext db) : RepositoryBase<LegalHold>(db), ILegalHoldRepository
{
    public async Task<IReadOnlyList<LegalHold>> GetActiveByDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.Where(h => h.DocumentId == documentId && h.Status == LegalHoldStatus.Active).ToListAsync(ct);
}
