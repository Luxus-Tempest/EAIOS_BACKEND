using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

public interface IKnowledgeItemRepository
{
    Task<KnowledgeItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<KnowledgeItem?> GetWithChunksAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<KnowledgeItem>> SearchAsync(string? search, KnowledgeItemType? type, KnowledgeItemStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeItem>> GetByPackIdAsync(Guid packId, CancellationToken ct = default);
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

    public async Task<PagedResult<KnowledgeItem>> SearchAsync(string? search, KnowledgeItemType? type, KnowledgeItemStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(i => i.Title.Contains(search) || i.Content!.Contains(search));
        if (type.HasValue) query = query.Where(i => i.Type == type);
        if (status.HasValue) query = query.Where(i => i.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<KnowledgeItem>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<KnowledgeItem>> GetByPackIdAsync(Guid packId, CancellationToken ct = default) =>
        (await Set.Where(i => i.PackId == packId).ToListAsync(ct)).AsReadOnly();

    public async Task<IReadOnlyList<KnowledgeItem>> GetBySourceDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        (await Set.Where(i => i.SourceDocumentId == documentId).ToListAsync(ct)).AsReadOnly();
}

public interface IKnowledgeChunkRepository
{
    Task<IReadOnlyList<KnowledgeChunk>> GetByItemIdAsync(Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeChunk>> GetUnembeddedAsync(int batchSize, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default);
    void Update(KnowledgeChunk chunk);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgeChunkRepository(EaiosDbContext db) : RepositoryBase<KnowledgeChunk>(db), IKnowledgeChunkRepository
{
    public async Task<IReadOnlyList<KnowledgeChunk>> GetByItemIdAsync(Guid itemId, CancellationToken ct = default) =>
        (await Set.Where(c => c.ItemId == itemId).OrderBy(c => c.ChunkIndex).ToListAsync(ct)).AsReadOnly();

    public async Task<IReadOnlyList<KnowledgeChunk>> GetUnembeddedAsync(int batchSize, CancellationToken ct = default) =>
        (await Set.Where(c => !c.IsEmbedded).Take(batchSize).ToListAsync(ct)).AsReadOnly();

    public async Task AddRangeAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default) =>
        await db.KnowledgeChunks.AddRangeAsync(chunks, ct);
}

public interface IKnowledgePackRepository
{
    Task<KnowledgePack?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<KnowledgePack>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(KnowledgePack pack, CancellationToken ct = default);
    void Update(KnowledgePack pack);
    void SoftDelete(KnowledgePack pack);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgePackRepository(EaiosDbContext db) : RepositoryBase<KnowledgePack>(db), IKnowledgePackRepository
{
    public override async Task<PagedResult<KnowledgePack>> GetPagedAsync(int page, int pageSize, System.Linq.Expressions.Expression<Func<KnowledgePack, bool>>? filter = null, Func<IQueryable<KnowledgePack>, IOrderedQueryable<KnowledgePack>>? orderBy = null, CancellationToken ct = default) =>
        await base.GetPagedAsync(page, pageSize, orderBy: q => q.OrderByDescending(p => p.CreatedAt), ct: ct);
}
