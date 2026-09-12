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
    int                    PageSize       = 20,
    /// <summary>Plafond de la personne ; au-dessus, seuls les identifiants explicitement accordés passent.</summary>
    ResourceClassification? MaxClassification = null,
    IReadOnlySet<Guid>?    ExplicitlyAllowed = null,
    IReadOnlySet<Guid>?    ExplicitlyDenied  = null);

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Document?> GetWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Document>> SearchAsync(DocumentQuery query, CancellationToken ct = default);
    /// <summary>Plein texte PostgreSQL sur le titre, la description et le texte extrait, par pertinence.</summary>
    Task<IReadOnlyList<Document>> FullTextSearchAsync(string query, Guid? workspaceId, string[]? classifications, int take, ResourceClassification? maxClassification = null, IReadOnlySet<Guid>? allowed = null, IReadOnlySet<Guid>? denied = null, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetTrashedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetTrashedAsync(ResourceClassification? maxClassification, IReadOnlySet<Guid>? allowed, IReadOnlySet<Guid>? denied, CancellationToken ct = default);
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
        query = ApplyVisibility(query, q.MaxClassification, q.ExplicitlyAllowed, q.ExplicitlyDenied);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.UpdatedAt)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);

        return new PagedResult<Document>(items, q.Page, q.PageSize, total);
    }

    public async Task<IReadOnlyList<Document>> FullTextSearchAsync(string query, Guid? workspaceId, string[]? classifications, int take, ResourceClassification? maxClassification = null, IReadOnlySet<Guid>? allowed = null, IReadOnlySet<Guid>? denied = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var q = ApplyVisibility(Set.AsNoTracking().Where(d => d.Status == ResourceStatus.Active), maxClassification, allowed, denied);
        if (workspaceId.HasValue) q = q.Where(d => d.WorkspaceId == workspaceId);
        if (classifications is { Length: > 0 })
        {
            var wanted = classifications
                .Select(c => Enum.TryParse<ResourceClassification>(c, true, out var parsed) ? parsed : (ResourceClassification?)null)
                .Where(c => c.HasValue).Select(c => c!.Value).ToList();
            if (wanted.Count > 0) q = q.Where(d => wanted.Contains(d.Classification));
        }

        // Le dictionnaire « french » couvre l'essentiel du corpus ; `plainto_tsquery`
        // accepte une requête telle que la personne l'a tapée, sans syntaxe.
        return await q
            .Select(d => new
            {
                Document = d,
                Rank = EF.Functions.ToTsVector("french", (d.Title ?? "") + " " + (d.Description ?? "") + " " + (d.ExtractedText ?? ""))
                    .Rank(EF.Functions.PlainToTsQuery("french", query))
            })
            .Where(x => EF.Functions.ToTsVector("french", (x.Document.Title ?? "") + " " + (x.Document.Description ?? "") + " " + (x.Document.ExtractedText ?? ""))
                .Matches(EF.Functions.PlainToTsQuery("french", query)))
            .OrderByDescending(x => x.Rank)
            .Take(take)
            .Select(x => x.Document)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Document>> GetTrashedAsync(CancellationToken ct = default) =>
        await Set.Where(d => d.Status == ResourceStatus.Trashed)
                 .OrderByDescending(d => d.UpdatedAt)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<Document>> GetTrashedAsync(ResourceClassification? maxClassification, IReadOnlySet<Guid>? allowed, IReadOnlySet<Guid>? denied, CancellationToken ct = default) =>
        await ApplyVisibility(Set.Where(d => d.Status == ResourceStatus.Trashed), maxClassification, allowed, denied)
                 .OrderByDescending(d => d.UpdatedAt)
                 .ToListAsync(ct);

    /// <summary>
    /// Le plafond de classification et les ACL nominatives, sous forme de
    /// filtre de requête : un document au-dessus du plafond n'apparaît que s'il
    /// est explicitement accordé, et jamais s'il est explicitement refusé.
    /// </summary>
    private static IQueryable<Document> ApplyVisibility(IQueryable<Document> query, ResourceClassification? max, IReadOnlySet<Guid>? allowed, IReadOnlySet<Guid>? denied)
    {
        if (max.HasValue)
        {
            var ceiling = max.Value;
            var allowedIds = (allowed ?? new HashSet<Guid>()).ToList();
            query = allowedIds.Count == 0
                ? query.Where(d => d.Classification <= ceiling)
                : query.Where(d => d.Classification <= ceiling || allowedIds.Contains(d.Id));
        }
        if (denied is { Count: > 0 })
        {
            var deniedIds = denied.ToList();
            query = query.Where(d => !deniedIds.Contains(d.Id));
        }
        return query;
    }

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

// ── IMetadataValueRepository ─────────────────────────────────────────────────

public interface IMetadataValueRepository
{
    Task<IReadOnlyList<MetadataValue>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default);
    Task<MetadataValue?> FindAsync(Guid resourceId, string fieldKey, CancellationToken ct = default);
    Task AddAsync(MetadataValue value, CancellationToken ct = default);
    void Update(MetadataValue value);
    void SoftDelete(MetadataValue value);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class MetadataValueRepository(EaiosDbContext db)
    : RepositoryBase<MetadataValue>(db), IMetadataValueRepository
{
    public async Task<IReadOnlyList<MetadataValue>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default) =>
        await Set.Where(m => m.ResourceId == resourceId)
                 .OrderBy(m => m.FieldKey)
                 .ToListAsync(ct);

    public async Task<MetadataValue?> FindAsync(Guid resourceId, string fieldKey, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(m => m.ResourceId == resourceId && m.FieldKey == fieldKey, ct);
}

// ── IMetadataTemplateRepository ──────────────────────────────────────────────

public interface IMetadataTemplateRepository
{
    Task<MetadataTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MetadataTemplate>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<IReadOnlyList<MetadataTemplate>> GetForResourceTypeAsync(string resourceType, CancellationToken ct = default);
    Task AddAsync(MetadataTemplate template, CancellationToken ct = default);
    void Update(MetadataTemplate template);
    void SoftDelete(MetadataTemplate template);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class MetadataTemplateRepository(EaiosDbContext db)
    : RepositoryBase<MetadataTemplate>(db), IMetadataTemplateRepository
{
    public async Task<IReadOnlyList<MetadataTemplate>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        var q = Set.AsQueryable();
        if (activeOnly) q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MetadataTemplate>> GetForResourceTypeAsync(string resourceType, CancellationToken ct = default)
    {
        // Un modèle sans type applicable déclaré vaut pour tous les types de ressource.
        var all = await Set.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
        return all.Where(t => t.ApplicableResourceTypes.Length == 0
                           || t.ApplicableResourceTypes.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
                  .ToList();
    }
}
