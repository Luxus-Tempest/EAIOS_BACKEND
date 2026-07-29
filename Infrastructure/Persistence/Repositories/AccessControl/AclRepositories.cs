using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Role?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    void Update(Role role);
    void SoftDelete(Role role);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class RoleRepository(EaiosDbContext db) : RepositoryBase<Role>(db), IRoleRepository
{
    public async Task<Role?> FindByNameAsync(string name, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(r => r.Name == name.ToLowerInvariant(), ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default) =>
        (await Set.OrderBy(r => r.Name).ToListAsync(ct)).AsReadOnly();

    public async Task<IReadOnlyList<Role>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var roleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId && !ur.IsExpired)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);
        return (await Set.Where(r => roleIds.Contains(r.Id)).ToListAsync(ct)).AsReadOnly();
    }
}

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default);
    Task<Permission?> FindByCodeAsync(string code, CancellationToken ct = default);
    Task AddAsync(Permission permission, CancellationToken ct = default);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class PermissionRepository(EaiosDbContext db) : RepositoryBase<Permission>(db), IPermissionRepository
{
    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default) =>
        (await Set.OrderBy(p => p.Module).ThenBy(p => p.Code).ToListAsync(ct)).AsReadOnly();

    public async Task<Permission?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(p => p.Code == code, ct);
}

public interface IUserRoleRepository
{
    Task<IReadOnlyList<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> HasRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    Task AddAsync(UserRole userRole, CancellationToken ct = default);
    void SoftDelete(UserRole userRole);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class UserRoleRepository(EaiosDbContext db) : RepositoryBase<UserRole>(db), IUserRoleRepository
{
    public async Task<IReadOnlyList<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await Set.Where(ur => ur.UserId == userId).ToListAsync(ct)).AsReadOnly();

    public async Task<bool> HasRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) =>
        await Set.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);
}

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Policy>> GetActiveAsync(CancellationToken ct = default);
    Task AddAsync(Policy policy, CancellationToken ct = default);
    void Update(Policy policy);
    void SoftDelete(Policy policy);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class PolicyRepository(EaiosDbContext db) : RepositoryBase<Policy>(db), IPolicyRepository
{
    public async Task<IReadOnlyList<Policy>> GetActiveAsync(CancellationToken ct = default) =>
        (await Set.Where(p => p.IsActive).OrderByDescending(p => p.Priority).ToListAsync(ct)).AsReadOnly();
}
