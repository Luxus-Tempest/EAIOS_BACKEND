using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;

namespace EAIOS.Api.Application.AccessControl;

public interface IAccessControlService
{
    Task<Role> CreateRoleAsync(Guid tenantId, string name, string? description, CancellationToken ct = default);
    Task<Role> UpdateRolePermissionsAsync(Guid roleId, string[] permissions, CancellationToken ct = default);
    Task<Policy> CreatePolicyAsync(Guid tenantId, string name, string? description, PolicyType type, string targetType, string conditionsJson, PolicyEffect effect, string[] permissions, int priority, Guid actorId, CancellationToken ct = default);
    Task<ResourceAcl> CreateAclAsync(Guid tenantId, Guid resourceId, string resourceType, Guid principalId, PrincipalType principalType, string[] permissions, AclEffect effect, string permissionLevel, Guid actorId, CancellationToken ct = default);
}

public sealed class AccessControlService(
    IRoleRepository roleRepo,
    IPolicyRepository policyRepo,
    IResourceAclRepository aclRepo) : IAccessControlService
{
    public async Task<Role> CreateRoleAsync(Guid tenantId, string name, string? description, CancellationToken ct = default)
    {
        var existing = await roleRepo.FindByNameAsync(name, ct);
        if (existing != null) throw new InvalidOperationException("ROLE_EXISTS");

        var role = Role.Create(tenantId, name, RoleScope.Organization, isSystem: false, description: description);
        await roleRepo.AddAsync(role, ct);
        await roleRepo.SaveAsync(ct);
        return role;
    }

    public async Task<Role> UpdateRolePermissionsAsync(Guid roleId, string[] permissions, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(roleId, ct) ?? throw new KeyNotFoundException("Rôle introuvable.");

        if (role.IsSystem)
            throw new InvalidOperationException("BUILTIN_ROLE_LOCKED");

        role.SetPermissions(permissions);
        roleRepo.Update(role);
        await roleRepo.SaveAsync(ct);
        return role;
    }

    public async Task<Policy> CreatePolicyAsync(Guid tenantId, string name, string? description, PolicyType type, string targetType, string conditionsJson, PolicyEffect effect, string[] permissions, int priority, Guid actorId, CancellationToken ct = default)
    {
        var policy = Policy.Create(tenantId, name, type, effect, permissions, actorId);
        policy.Update(null, description, null, null, conditionsJson, null);
        await policyRepo.AddAsync(policy, ct);
        await policyRepo.SaveAsync(ct);
        return policy;
    }

    public async Task<ResourceAcl> CreateAclAsync(Guid tenantId, Guid resourceId, string resourceType, Guid principalId, PrincipalType principalType, string[] permissions, AclEffect effect, string permissionLevel, Guid actorId, CancellationToken ct = default)
    {
        var acl = ResourceAcl.Create(tenantId, resourceId, resourceType, principalType, principalId, permissions, effect, actorId, null);
        await aclRepo.AddAsync(acl, ct);
        await aclRepo.SaveAsync(ct);
        return acl;
    }
}
