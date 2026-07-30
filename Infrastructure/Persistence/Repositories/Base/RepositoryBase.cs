using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Base;

/// <summary>
/// Repository générique fournissant CRUD + pagination pour tout type TenantEntity.
/// Les Global Query Filters du DbContext assurent automatiquement l'isolation tenant.
/// </summary>
public abstract class RepositoryBase<T>(EaiosDbContext db) where T : TenantEntity
{
    protected readonly EaiosDbContext Db  = db;
    protected DbSet<T>                Set => Db.Set<T>();

    // ── Lecture ────────────────────────────────────────────────────────────────

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await Set.AnyAsync(predicate, ct);

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate == null ? await Set.CountAsync(ct) : await Set.CountAsync(predicate, ct);

    public virtual async Task<PagedResult<T>> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<T, bool>>?              filter  = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        CancellationToken ct = default)
    {
        IQueryable<T> query = Set.AsNoTracking();
        if (filter  is not null) query = query.Where(filter);
        if (orderBy is not null) query = orderBy(query);
        else                    query = query.OrderByDescending(e => e.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items, page, pageSize, total);
    }

    // ── Écriture ───────────────────────────────────────────────────────────────

    public virtual async Task AddAsync(T entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public virtual async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
        await Set.AddRangeAsync(entities, ct);

    public virtual void Update(T entity)
    {
        Set.Attach(entity);
        Db.Entry(entity).State = EntityState.Modified;
    }

    /// <summary>
    /// Soft-delete : marque IsDeleted = true.
    /// Le SaveChanges du DbContext enrichira DeletedAt/DeletedBy.
    /// </summary>
    public virtual void SoftDelete(T entity)
    {
        typeof(T).GetProperty(nameof(TenantEntity.IsDeleted))!.SetValue(entity, true);
        Update(entity);
    }

    public virtual async Task<int> SaveAsync(CancellationToken ct = default) =>
        await Db.SaveChangesAsync(ct);
}
