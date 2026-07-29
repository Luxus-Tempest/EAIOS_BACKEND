using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Shared.Primitives;
using System.Linq.Expressions;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Base;

/// <summary>
/// Generic repository base providing standard CRUD + pagination for all TenantEntity types.
/// Global Query Filters in EaiosDbContext handle tenant isolation automatically.
/// </summary>
public abstract class RepositoryBase<T>(EaiosDbContext db)
    where T : TenantEntity
{
    protected readonly EaiosDbContext Db = db;
    protected Microsoft.EntityFrameworkCore.DbSet<T> Set => Db.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await Set.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate == null
            ? await Set.CountAsync(ct)
            : await Set.CountAsync(predicate, ct);

    public virtual async Task<Application.Common.Models.PagedResult<T>> GetPagedAsync(
        int page, int pageSize,
        Expression<Func<T, bool>>? filter = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        CancellationToken ct = default)
    {
        IQueryable<T> query = Set;
        if (filter is not null) query = query.Where(filter);
        if (orderBy is not null) query = orderBy(query);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new Application.Common.Models.PagedResult<T>(items, page, pageSize, total);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public void Update(T entity) => Set.Update(entity);

    /// <summary>Soft-delete: sets IsDeleted = true via EF change tracking.</summary>
    public void SoftDelete(T entity)
    {
        // The DbContext SaveChanges interceptor will handle the soft-delete mutation
        entity.GetType().GetProperty(nameof(TenantEntity.IsDeleted))!
            .SetValue(entity, true);
        Update(entity);
    }

    public async Task<int> SaveAsync(CancellationToken ct = default) =>
        await Db.SaveChangesAsync(ct);
}

// EF Core using statement helper
file static class EntityFrameworkExtensions
{
    public static async Task<T?> FirstOrDefaultAsync<T>(
        this Microsoft.EntityFrameworkCore.DbSet<T> set,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct) where T : class =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(set, predicate, ct);

    public static async Task<bool> AnyAsync<T>(
        this Microsoft.EntityFrameworkCore.DbSet<T> set,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct) where T : class =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(set, predicate, ct);

    public static async Task<int> CountAsync<T>(
        this Microsoft.EntityFrameworkCore.DbSet<T> set,
        CancellationToken ct) where T : class =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(set, ct);

    public static async Task<int> CountAsync<T>(
        this Microsoft.EntityFrameworkCore.DbSet<T> set,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct) where T : class =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(set, predicate, ct);

    public static async Task<List<T>> ToListAsync<T>(
        this IQueryable<T> query, CancellationToken ct) =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(query, ct);
}
