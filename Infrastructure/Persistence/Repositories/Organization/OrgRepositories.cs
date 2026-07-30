using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;

// ── IWorkspaceRepository ─────────────────────────────────────────────────────

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Workspace>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<Workspace>> GetByMemberAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Workspace workspace, CancellationToken ct = default);
    void Update(Workspace workspace);
    void SoftDelete(Workspace workspace);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class WorkspaceRepository(EaiosDbContext db) : RepositoryBase<Workspace>(db), IWorkspaceRepository
{
    public async Task<IReadOnlyList<Workspace>> GetByMemberAsync(Guid userId, CancellationToken ct = default)
    {
        var workspaceIds = await db.Memberships
            .Where(m => m.UserId == userId && m.WorkspaceId != null && m.Status == MembershipStatus.Active)
            .Select(m => m.WorkspaceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return await Set.Where(w => workspaceIds.Contains(w.Id))
                        .OrderBy(w => w.Name)
                        .ToListAsync(ct);
    }
}

// ── IDepartmentRepository ────────────────────────────────────────────────────

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Department dept, CancellationToken ct = default);
    void Update(Department dept);
    void SoftDelete(Department dept);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class DepartmentRepository(EaiosDbContext db) : RepositoryBase<Department>(db), IDepartmentRepository
{
    public async Task<IReadOnlyList<Department>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default) =>
        await Set.Where(d => d.ParentId == parentId)
                 .OrderBy(d => d.Name)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<Department>> GetAllAsync(CancellationToken ct = default) =>
        await Set.OrderBy(d => d.Name).ToListAsync(ct);
}

// ── IMembershipRepository ────────────────────────────────────────────────────

public interface IMembershipRepository
{
    Task<Membership?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Membership?> FindAsync(Guid userId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByDepartmentAsync(Guid departmentId, CancellationToken ct = default);
    Task AddAsync(Membership membership, CancellationToken ct = default);
    void Update(Membership membership);
    void SoftDelete(Membership membership);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class MembershipRepository(EaiosDbContext db) : RepositoryBase<Membership>(db), IMembershipRepository
{
    public async Task<Membership?> FindAsync(Guid userId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(m =>
            m.UserId == userId &&
            m.WorkspaceId == workspaceId &&
            m.DepartmentId == departmentId, ct);

    public async Task<IReadOnlyList<Membership>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
        await Set.Where(m => m.UserId == userId && m.Status == MembershipStatus.Active).ToListAsync(ct);

    public async Task<IReadOnlyList<Membership>> GetByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        await Set.Where(m => m.WorkspaceId == workspaceId && m.Status == MembershipStatus.Active)
                 .OrderBy(m => m.JoinedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Membership>> GetByDepartmentAsync(Guid departmentId, CancellationToken ct = default) =>
        await Set.Where(m => m.DepartmentId == departmentId && m.Status == MembershipStatus.Active)
                 .OrderBy(m => m.JoinedAt).ToListAsync(ct);
}
