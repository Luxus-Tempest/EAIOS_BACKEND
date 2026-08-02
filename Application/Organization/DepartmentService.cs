using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;

namespace EAIOS.Api.Application.Organization;

public interface IDepartmentService
{
    Task<Department> CreateDepartmentAsync(Guid tenantId, string name, Guid ownerId, Guid? parentId, string? description, CancellationToken ct = default);
    Task<Department> UpdateDepartmentAsync(Guid id, string name, string? description, Guid? managerId, string? code, CancellationToken ct = default);
    Task DeleteDepartmentAsync(Guid id, CancellationToken ct = default);
    Task<Membership> AddMemberAsync(Guid tenantId, Guid departmentId, Guid userId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid departmentId, Guid userId, CancellationToken ct = default);
}

public sealed class DepartmentService(
    IDepartmentRepository departmentRepo,
    IMembershipRepository membershipRepo) : IDepartmentService
{
    public async Task<Department> CreateDepartmentAsync(Guid tenantId, string name, Guid ownerId, Guid? parentId, string? description, CancellationToken ct = default)
    {
        var all = await departmentRepo.GetAllAsync(ct);
        if (all.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && d.ParentId == parentId))
            throw new InvalidOperationException("NAME_ALREADY_EXISTS");

        var dept = Department.Create(tenantId, name, ownerId, parentId, description);
        await departmentRepo.AddAsync(dept, ct);
        await departmentRepo.SaveAsync(ct);
        return dept;
    }

    public async Task<Department> UpdateDepartmentAsync(Guid id, string name, string? description, Guid? managerId, string? code, CancellationToken ct = default)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();

        if (!string.IsNullOrWhiteSpace(name) && !name.Equals(dept.Name, StringComparison.OrdinalIgnoreCase))
        {
            var all = await departmentRepo.GetAllAsync(ct);
            if (all.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && d.ParentId == dept.ParentId && d.Id != dept.Id))
                throw new InvalidOperationException("NAME_ALREADY_EXISTS");
        }

        dept.Update(name, description, managerId, code);
        departmentRepo.Update(dept);
        await departmentRepo.SaveAsync(ct);
        return dept;
    }

    public async Task DeleteDepartmentAsync(Guid id, CancellationToken ct = default)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        
        var children = await departmentRepo.GetChildrenAsync(id, ct);
        if (children.Count > 0)
            throw new InvalidOperationException("HAS_CHILDREN");

        departmentRepo.SoftDelete(dept);
        await departmentRepo.SaveAsync(ct);
    }

    public async Task<Membership> AddMemberAsync(Guid tenantId, Guid departmentId, Guid userId, CancellationToken ct = default)
    {
        var existing = await membershipRepo.FindAsync(userId, null, departmentId, ct);
        if (existing != null) throw new InvalidOperationException("ALREADY_MEMBER");

        var membership = Membership.Create(tenantId, userId, MembershipType.Member, departmentId: departmentId);
        await membershipRepo.AddAsync(membership, ct);
        await membershipRepo.SaveAsync(ct);
        return membership;
    }

    public async Task RemoveMemberAsync(Guid departmentId, Guid userId, CancellationToken ct = default)
    {
        var membership = await membershipRepo.FindAsync(userId, null, departmentId, ct) ?? throw new KeyNotFoundException();
        membershipRepo.SoftDelete(membership);
        await membershipRepo.SaveAsync(ct);
    }
}
