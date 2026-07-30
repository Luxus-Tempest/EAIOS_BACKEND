using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;

// ── IRoleRepository ──────────────────────────────────────────────────────────

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Role?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<PagedResult<Role>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
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
        await Set.OrderBy(r => r.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Role>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var roleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToListAsync(ct);

        return await Set.Where(r => roleIds.Contains(r.Id)).OrderBy(r => r.Name).ToListAsync(ct);
    }
}

// ── IPermissionRepository ────────────────────────────────────────────────────

public interface IPermissionRepository
{
    Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Permission?> FindByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByModuleAsync(string module, CancellationToken ct = default);
    Task AddAsync(Permission permission, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Permission> permissions, CancellationToken ct = default);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class PermissionRepository(EaiosDbContext db) : RepositoryBase<Permission>(db), IPermissionRepository
{
    public async Task<Permission?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(p => p.Code == code, ct);

    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default) =>
        await Set.OrderBy(p => p.Module).ThenBy(p => p.Code).ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> GetByModuleAsync(string module, CancellationToken ct = default) =>
        await Set.Where(p => p.Module == module).OrderBy(p => p.Code).ToListAsync(ct);

    public async Task AddRangeAsync(IEnumerable<Permission> permissions, CancellationToken ct = default) =>
        await Set.AddRangeAsync(permissions, ct);
}

// ── IUserRoleRepository ──────────────────────────────────────────────────────

public interface IUserRoleRepository
{
    Task<UserRole?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<UserRole>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> HasRoleAsync(Guid userId, Guid roleId, Guid? workspaceId, CancellationToken ct = default);
    Task AddAsync(UserRole userRole, CancellationToken ct = default);
    void SoftDelete(UserRole userRole);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class UserRoleRepository(EaiosDbContext db) : RepositoryBase<UserRole>(db), IUserRoleRepository
{
    public async Task<IReadOnlyList<UserRole>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
        await Set.Where(ur => ur.UserId == userId).OrderByDescending(ur => ur.AssignedAt).ToListAsync(ct);

    public async Task<bool> HasRoleAsync(Guid userId, Guid roleId, Guid? workspaceId, CancellationToken ct = default) =>
        await Set.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId && ur.WorkspaceId == workspaceId, ct);
}

// ── IPolicyRepository ────────────────────────────────────────────────────────

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Policy>> GetActiveAsync(CancellationToken ct = default);
    Task<PagedResult<Policy>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(Policy policy, CancellationToken ct = default);
    void Update(Policy policy);
    void SoftDelete(Policy policy);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class PolicyRepository(EaiosDbContext db) : RepositoryBase<Policy>(db), IPolicyRepository
{
    public async Task<IReadOnlyList<Policy>> GetActiveAsync(CancellationToken ct = default) =>
        await Set.Where(p => p.IsActive)
                 .OrderByDescending(p => p.Priority)
                 .ToListAsync(ct);
}

// ── IResourceAclRepository ───────────────────────────────────────────────────

public interface IResourceAclRepository
{
    Task<IReadOnlyList<ResourceAcl>> GetByResourceAsync(Guid resourceId, string resourceType, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceAcl>> GetByPrincipalAsync(Guid principalId, PrincipalType principalType, CancellationToken ct = default);
    Task AddAsync(ResourceAcl acl, CancellationToken ct = default);
    void SoftDelete(ResourceAcl acl);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class ResourceAclRepository(EaiosDbContext db) : RepositoryBase<ResourceAcl>(db), IResourceAclRepository
{
    public async Task<IReadOnlyList<ResourceAcl>> GetByResourceAsync(Guid resourceId, string resourceType, CancellationToken ct = default) =>
        await Set.Where(a => a.ResourceId == resourceId && a.ResourceType == resourceType).ToListAsync(ct);

    public async Task<IReadOnlyList<ResourceAcl>> GetByPrincipalAsync(Guid principalId, PrincipalType principalType, CancellationToken ct = default) =>
        await Set.Where(a => a.PrincipalId == principalId && a.PrincipalType == principalType).ToListAsync(ct);
}
