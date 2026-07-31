using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

// ── IKnowledgeItemRepository ─────────────────────────────────────────────────

public interface IKnowledgeItemRepository
{
    Task<KnowledgeItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<KnowledgeItem?> GetWithChunksAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<KnowledgeItem>> SearchAsync(string? q, KnowledgeItemType? type, KnowledgeItemStatus? status, Guid? packId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeItem>> GetBySourceDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task AddAsync(KnowledgeItem item, CancellationToken ct = default);
    void Update(KnowledgeItem item);
    void SoftDelete(KnowledgeItem item);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgeItemRepository(EaiosDbContext db) : RepositoryBase<KnowledgeItem>(db), IKnowledgeItemRepository
{
    public async Task<KnowledgeItem?> GetWithChunksAsync(Guid id, CancellationToken ct = default) =>
        await Set.Include(i => i.Chunks).FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<PagedResult<KnowledgeItem>> SearchAsync(string? q, KnowledgeItemType? type, KnowledgeItemStatus? status, Guid? packId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(i => i.Title.Contains(q));
        if (type.HasValue)   query = query.Where(i => i.Type   == type);
        if (status.HasValue) query = query.Where(i => i.Status == status);
        if (packId.HasValue) query = query.Where(i => i.PackId == packId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<KnowledgeItem>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<KnowledgeItem>> GetBySourceDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.Where(i => i.SourceDocumentId == documentId).ToListAsync(ct);
}

// ── IKnowledgeChunkRepository ────────────────────────────────────────────────

public interface IKnowledgeChunkRepository
{
    Task<IReadOnlyList<KnowledgeChunk>> GetByItemAsync(Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeChunk>> GetPendingEmbeddingAsync(int batchSize, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default);
    void Update(KnowledgeChunk chunk);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgeChunkRepository(EaiosDbContext db) : RepositoryBase<KnowledgeChunk>(db), IKnowledgeChunkRepository
{
    public async Task<IReadOnlyList<KnowledgeChunk>> GetByItemAsync(Guid itemId, CancellationToken ct = default) =>
        await Set.Where(c => c.ItemId == itemId).OrderBy(c => c.ChunkIndex).ToListAsync(ct);

    public async Task<IReadOnlyList<KnowledgeChunk>> GetPendingEmbeddingAsync(int batchSize, CancellationToken ct = default) =>
        await Set.Where(c => !c.IsEmbedded).Take(batchSize).ToListAsync(ct);

    public override async Task AddRangeAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default) =>
        await db.KnowledgeChunks.AddRangeAsync(chunks, ct);
}

// ── IKnowledgePackRepository ─────────────────────────────────────────────────

public interface IKnowledgePackRepository
{
    Task<KnowledgePack?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<KnowledgePack>> GetPagedAsync(
        int page,
        int pageSize,
        System.Linq.Expressions.Expression<Func<KnowledgePack, bool>>? filter = null,
        Func<IQueryable<KnowledgePack>, IOrderedQueryable<KnowledgePack>>? orderBy = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgePack>> GetPublicAsync(CancellationToken ct = default);
    Task AddAsync(KnowledgePack pack, CancellationToken ct = default);
    void Update(KnowledgePack pack);
    void SoftDelete(KnowledgePack pack);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgePackRepository(EaiosDbContext db) : RepositoryBase<KnowledgePack>(db), IKnowledgePackRepository
{
    public async Task<IReadOnlyList<KnowledgePack>> GetPublicAsync(CancellationToken ct = default) =>
        await Set.Where(p => p.IsPublic && p.Status == KnowledgePackStatus.Published)
                 .OrderBy(p => p.Name).ToListAsync(ct);
}
