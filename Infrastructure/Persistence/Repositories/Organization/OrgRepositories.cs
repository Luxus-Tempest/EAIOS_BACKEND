using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Workspace>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetMembersAsync(Guid workspaceId, CancellationToken ct = default);
    Task AddAsync(Workspace workspace, CancellationToken ct = default);
    void Update(Workspace workspace);
    void SoftDelete(Workspace workspace);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class WorkspaceRepository(EaiosDbContext db) : RepositoryBase<Workspace>(db), IWorkspaceRepository
{
    public async Task<IReadOnlyList<Membership>> GetMembersAsync(Guid workspaceId, CancellationToken ct = default) =>
        (await db.Memberships.Where(m => m.WorkspaceId == workspaceId && m.Status == MembershipStatus.Active)
            .ToListAsync(ct)).AsReadOnly();
}

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Department>> GetPagedAsync(Guid? parentId, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(Department dept, CancellationToken ct = default);
    void Update(Department dept);
    void SoftDelete(Department dept);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class DepartmentRepository(EaiosDbContext db) : RepositoryBase<Department>(db), IDepartmentRepository
{
    public async Task<PagedResult<Department>> GetPagedAsync(Guid? parentId, int page, int pageSize, CancellationToken ct = default) =>
        await GetPagedAsync(page, pageSize,
            filter: parentId.HasValue ? d => d.ParentId == parentId : null,
            orderBy: q => q.OrderBy(d => d.Name), ct: ct);
}

public interface IMembershipRepository
{
    Task<Membership?> FindAsync(Guid userId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Membership membership, CancellationToken ct = default);
    void Update(Membership membership);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class MembershipRepository(EaiosDbContext db) : RepositoryBase<Membership>(db), IMembershipRepository
{
    public async Task<Membership?> FindAsync(Guid userId, Guid? workspaceId, Guid? departmentId, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(m => m.UserId == userId && m.WorkspaceId == workspaceId && m.DepartmentId == departmentId, ct);

    public async Task<IReadOnlyList<Membership>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await Set.Where(m => m.UserId == userId && m.Status == MembershipStatus.Active).ToListAsync(ct)).AsReadOnly();
}
