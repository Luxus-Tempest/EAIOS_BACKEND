using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

// ── IKnowledgeItemRepository ─────────────────────────────────────────────────

public interface IKnowledgeItemRepository
{
    Task<KnowledgeItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Toutes les fiches d'un pack — pour les détacher avant sa suppression.</summary>
    Task<IReadOnlyList<KnowledgeItem>> GetByPackAsync(Guid packId, CancellationToken ct = default);
    Task<KnowledgeItem?> GetWithChunksAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<KnowledgeItem>> SearchAsync(string? q, KnowledgeItemType? type, KnowledgeItemStatus? status, Guid? packId, int page, int pageSize, CancellationToken ct = default, EAIOS.Api.Domain.Resource.ResourceClassification? maxClassification = null);
    Task<IReadOnlyList<KnowledgeItem>> GetBySourceDocumentAsync(Guid documentId, CancellationToken ct = default);
    /// <summary>Plein texte PostgreSQL sur le titre, le résumé et le contenu, par pertinence.</summary>
    Task<IReadOnlyList<KnowledgeItem>> FullTextSearchAsync(string query, KnowledgeItemStatus? status, int take, CancellationToken ct = default, EAIOS.Api.Domain.Resource.ResourceClassification? maxClassification = null);
    Task AddAsync(KnowledgeItem item, CancellationToken ct = default);
    void Update(KnowledgeItem item);
    void SoftDelete(KnowledgeItem item);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgeItemRepository(EaiosDbContext db) : RepositoryBase<KnowledgeItem>(db), IKnowledgeItemRepository
{
    public async Task<KnowledgeItem?> GetWithChunksAsync(Guid id, CancellationToken ct = default) =>
        await Set.Include(i => i.Chunks).FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<KnowledgeItem>> GetByPackAsync(Guid packId, CancellationToken ct = default) =>
        await Set.Where(i => i.PackId == packId).ToListAsync(ct);

    public async Task<PagedResult<KnowledgeItem>> SearchAsync(string? q, KnowledgeItemType? type, KnowledgeItemStatus? status, Guid? packId, int page, int pageSize, CancellationToken ct = default, EAIOS.Api.Domain.Resource.ResourceClassification? maxClassification = null)
    {
        var query = Set.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(i => i.Title.Contains(q));
        if (type.HasValue)   query = query.Where(i => i.Type   == type);
        if (status.HasValue) query = query.Where(i => i.Status == status);
        if (packId.HasValue) query = query.Where(i => i.PackId == packId);
        query = WithinCeiling(query, maxClassification);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<KnowledgeItem>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<KnowledgeItem>> GetBySourceDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await Set.Where(i => i.SourceDocumentId == documentId).ToListAsync(ct);

    /// <summary>
    /// Une fiche dérivée d'un document hérite de sa classification : au-dessus
    /// du plafond de la personne, elle n'apparaît pas. Une fiche sans document
    /// source est traitée comme <c>Internal</c>.
    /// </summary>
    private IQueryable<KnowledgeItem> WithinCeiling(IQueryable<KnowledgeItem> query, EAIOS.Api.Domain.Resource.ResourceClassification? max)
    {
        if (!max.HasValue) return query;
        var ceiling = max.Value;
        var documents = Db.Documents;
        return query.Where(i => i.SourceDocumentId == null
                             ? ceiling >= EAIOS.Api.Domain.Resource.ResourceClassification.Internal
                             : documents.Any(d => d.Id == i.SourceDocumentId && d.Classification <= ceiling));
    }

    public async Task<IReadOnlyList<KnowledgeItem>> FullTextSearchAsync(string query, KnowledgeItemStatus? status, int take, CancellationToken ct = default, EAIOS.Api.Domain.Resource.ResourceClassification? maxClassification = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var q = WithinCeiling(Set.AsNoTracking().AsQueryable(), maxClassification);
        if (status.HasValue) q = q.Where(i => i.Status == status);

        return await q
            .Select(i => new
            {
                Item = i,
                Rank = EF.Functions.ToTsVector("french", i.Title + " " + (i.Summary ?? "") + " " + (i.Content ?? ""))
                    .Rank(EF.Functions.PlainToTsQuery("french", query))
            })
            .Where(x => EF.Functions.ToTsVector("french", x.Item.Title + " " + (x.Item.Summary ?? "") + " " + (x.Item.Content ?? ""))
                .Matches(EF.Functions.PlainToTsQuery("french", query)))
            .OrderByDescending(x => x.Rank)
            .Take(take)
            .Select(x => x.Item)
            .ToListAsync(ct);
    }
}

// ── IKnowledgeChunkRepository ────────────────────────────────────────────────

public interface IKnowledgeChunkRepository
{
    Task<IReadOnlyList<KnowledgeChunk>> GetByItemAsync(Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeChunk>> GetPendingEmbeddingAsync(int batchSize, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default);
    void Update(KnowledgeChunk chunk);
    void SoftDelete(KnowledgeChunk chunk);
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
    /// <summary>Liste filtrée par statut et par nom, les plus récents d'abord.</summary>
    Task<PagedResult<KnowledgePack>> SearchAsync(string? q, KnowledgePackStatus? status, int page, int pageSize, CancellationToken ct = default);
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

    public async Task<PagedResult<KnowledgePack>> SearchAsync(string? q, KnowledgePackStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsQueryable();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, needle) || (p.Description != null && EF.Functions.ILike(p.Description, needle)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<KnowledgePack>(items, page, pageSize, total);
    }

}

// ── IKnowledgeRelationRepository ─────────────────────────────────────────────

public interface IKnowledgeRelationRepository
{
    Task<KnowledgeRelation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeRelation>> GetByItemAsync(Guid itemId, bool includeIncoming = true, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeRelation>> GetOutgoingAsync(Guid sourceItemId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeRelation>> GetByItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
    Task<bool> ExistsBetweenAsync(Guid sourceId, Guid targetId, string relationType, CancellationToken ct = default);
    Task AddAsync(KnowledgeRelation relation, CancellationToken ct = default);
    void Update(KnowledgeRelation relation);
    void SoftDelete(KnowledgeRelation relation);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class KnowledgeRelationRepository(EaiosDbContext db)
    : RepositoryBase<KnowledgeRelation>(db), IKnowledgeRelationRepository
{
    public async Task<IReadOnlyList<KnowledgeRelation>> GetByItemAsync(
        Guid itemId, bool includeIncoming = true, CancellationToken ct = default) =>
        await Set.Where(r => r.SourceItemId == itemId || (includeIncoming && r.TargetItemId == itemId))
                 .OrderBy(r => r.RelationType)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<KnowledgeRelation>> GetOutgoingAsync(
        Guid sourceItemId, CancellationToken ct = default) =>
        await Set.Where(r => r.SourceItemId == sourceItemId)
                 .OrderBy(r => r.RelationType)
                 .ToListAsync(ct);

    /// <summary>Charge en une requête toutes les arêtes touchant un lot de noeuds — utilisé par la traversée du graphe.</summary>
    public async Task<IReadOnlyList<KnowledgeRelation>> GetByItemsAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) return [];
        return await Set.Where(r => itemIds.Contains(r.SourceItemId) || itemIds.Contains(r.TargetItemId))
                        .ToListAsync(ct);
    }

    public async Task<bool> ExistsBetweenAsync(Guid sourceId, Guid targetId, string relationType, CancellationToken ct = default) =>
        await Set.AnyAsync(r => r.SourceItemId == sourceId
                             && r.TargetItemId == targetId
                             && r.RelationType == relationType, ct);
}
