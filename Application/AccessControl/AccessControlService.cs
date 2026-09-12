using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Persistence.Seeds;

namespace EAIOS.Api.Application.AccessControl;

public interface IAccessControlService
{
    // ── Rôles ─────────────────────────────────────────────────────────────────
    Task<Role> CreateRoleAsync(Guid tenantId, string name, string? description, CancellationToken ct = default);
    Task<Role> UpdateRoleAsync(Guid roleId, string? displayName, string? description, string? color, CancellationToken ct = default);
    Task<Role> UpdateRolePermissionsAsync(Guid roleId, string[] permissions, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>
    /// Retrouve un rôle depuis ce qu'un client envoie : son identifiant ou son nom
    /// technique (<c>org.member</c>). Les deux formes circulent — l'invitation
    /// envoyait l'identifiant, le contrat demandait le nom.
    /// </summary>
    Task<Role?> ResolveRoleAsync(string? idOrName, CancellationToken ct = default);

    // ── Attributions ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserRole>> GetRoleHoldersAsync(Guid roleId, CancellationToken ct = default);
    Task<UserRole> AssignRoleAsync(Guid tenantId, Guid roleId, Guid userId, Guid actorId,
        Guid? workspaceId = null, Guid? departmentId = null, DateTime? expiresAt = null, CancellationToken ct = default);
    Task RevokeRoleAsync(Guid roleId, Guid userId, CancellationToken ct = default);

    // ── Catalogue ─────────────────────────────────────────────────────────────
    IReadOnlyList<PermissionCatalogDto> GetPermissionCatalog();

    // ── Politiques ────────────────────────────────────────────────────────────
    Task<Policy> CreatePolicyAsync(Guid tenantId, string name, string? description, PolicyType type, string targetType, string conditionsJson, PolicyEffect effect, string[] permissions, int priority, Guid actorId, CancellationToken ct = default);
    Task<Policy> UpdatePolicyAsync(Guid policyId, string? name, string? description, PolicyEffect? effect, string[]? permissions, string? condition, bool? isActive, int? priority, PrincipalType? principalType, string? principalId, string? resourceType, CancellationToken ct = default);
    Task DeletePolicyAsync(Guid policyId, CancellationToken ct = default);

    // ── ACL ───────────────────────────────────────────────────────────────────
    Task<ResourceAcl> CreateAclAsync(Guid tenantId, Guid resourceId, string resourceType, Guid principalId, PrincipalType principalType, string[] permissions, AclEffect effect, string permissionLevel, Guid actorId, CancellationToken ct = default);
    Task DeleteAclAsync(Guid aclId, CancellationToken ct = default);
}

public sealed class AccessControlService(
    IRoleRepository roleRepo,
    IUserRoleRepository userRoleRepo,
    IUserRepository userRepo,
    IPolicyRepository policyRepo,
    IResourceAclRepository aclRepo) : IAccessControlService
{
    // ═════════════════════════════════════════════════════════════════════════
    // RÔLES
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<Role> CreateRoleAsync(Guid tenantId, string name, string? description, CancellationToken ct = default)
    {
        var existing = await roleRepo.FindByNameAsync(name, ct);
        if (existing != null) throw new InvalidOperationException("ROLE_EXISTS");

        var role = Role.Create(tenantId, name, RoleScope.Organization, isSystem: false, description: description);
        await roleRepo.AddAsync(role, ct);
        await roleRepo.SaveAsync(ct);
        return role;
    }

    public async Task<Role> UpdateRoleAsync(Guid roleId, string? displayName, string? description, string? color, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(roleId, ct) ?? throw new KeyNotFoundException("Rôle introuvable.");

        // Un rôle système garde son identité ; seul son habillage est modifiable.
        role.Update(role.IsSystem ? null : displayName, description, color);
        roleRepo.Update(role);
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

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(roleId, ct) ?? throw new KeyNotFoundException("Rôle introuvable.");

        if (role.IsSystem)
            throw new InvalidOperationException("BUILTIN_ROLE_LOCKED");

        // Les attributions partent avec le rôle : un rôle supprimé ne doit plus
        // conférer de droit à personne, même par une ligne orpheline.
        var holders = await userRoleRepo.GetByRoleAsync(roleId, ct);
        foreach (var assignment in holders)
            userRoleRepo.SoftDelete(assignment);

        roleRepo.SoftDelete(role);
        await userRoleRepo.SaveAsync(ct);
        await roleRepo.SaveAsync(ct);
    }

    public async Task<Role?> ResolveRoleAsync(string? idOrName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;

        if (Guid.TryParse(idOrName, out var id))
            return await roleRepo.GetByIdAsync(id, ct);

        return await roleRepo.FindByNameAsync(idOrName.Trim(), ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ATTRIBUTIONS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
        await userRoleRepo.GetByUserAsync(userId, ct);

    public async Task<IReadOnlyList<UserRole>> GetRoleHoldersAsync(Guid roleId, CancellationToken ct = default) =>
        await userRoleRepo.GetByRoleAsync(roleId, ct);

    public async Task<UserRole> AssignRoleAsync(Guid tenantId, Guid roleId, Guid userId, Guid actorId,
        Guid? workspaceId = null, Guid? departmentId = null, DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(roleId, ct) ?? throw new KeyNotFoundException("Rôle introuvable.");
        _ = await userRepo.GetByIdAsync(userId, ct) ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
            throw new ArgumentException("La date d'expiration doit être dans le futur.");

        // Une attribution vivante sur la même portée suffit : on ne double pas.
        var existing = await userRoleRepo.FindAsync(userId, roleId, ct);
        if (existing.Any(a => a.WorkspaceId == workspaceId && a.DepartmentId == departmentId && !a.IsExpired))
            throw new InvalidOperationException("ROLE_ALREADY_ASSIGNED");

        var assignment = UserRole.Create(tenantId, userId, roleId, role.Name, actorId, workspaceId, departmentId, expiresAt);
        await userRoleRepo.AddAsync(assignment, ct);

        role.IncrementUserCount();
        roleRepo.Update(role);

        await userRoleRepo.SaveAsync(ct);
        await roleRepo.SaveAsync(ct);
        return assignment;
    }

    public async Task RevokeRoleAsync(Guid roleId, Guid userId, CancellationToken ct = default)
    {
        var role = await roleRepo.GetByIdAsync(roleId, ct) ?? throw new KeyNotFoundException("Rôle introuvable.");

        var assignments = await userRoleRepo.FindAsync(userId, roleId, ct);
        if (assignments.Count == 0)
            throw new KeyNotFoundException("Cette personne ne porte pas ce rôle.");

        foreach (var assignment in assignments)
        {
            userRoleRepo.SoftDelete(assignment);
            role.DecrementUserCount();
        }

        roleRepo.Update(role);
        await userRoleRepo.SaveAsync(ct);
        await roleRepo.SaveAsync(ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CATALOGUE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Le catalogue vit dans le code (<see cref="SystemPermissionsSeed.AllPermissions"/>) :
    /// c'est la même liste qui est semée dans chaque organisation. L'exposer évite
    /// au frontend d'en tenir une copie qui dérive.
    /// </summary>
    public IReadOnlyList<PermissionCatalogDto> GetPermissionCatalog() =>
        SystemPermissionsSeed.AllPermissions
            .GroupBy(p => p.Module)
            .Select(g => new PermissionCatalogDto(
                g.Key,
                g.Select(p => new PermissionDto(Guid.Empty, p.Code, p.Name, p.Description, p.Module, IsSystem: true)).ToList()))
            .ToList();

    // ═════════════════════════════════════════════════════════════════════════
    // POLITIQUES
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<Policy> CreatePolicyAsync(Guid tenantId, string name, string? description, PolicyType type, string targetType, string conditionsJson, PolicyEffect effect, string[] permissions, int priority, Guid actorId, CancellationToken ct = default)
    {
        var policy = Policy.Create(tenantId, name, type, effect, permissions, actorId);
        policy.Update(null, description, null, null, conditionsJson, null);
        policy.SetTarget(PrincipalType.All, null, string.IsNullOrWhiteSpace(targetType) ? null : targetType, priority);
        await policyRepo.AddAsync(policy, ct);
        await policyRepo.SaveAsync(ct);
        return policy;
    }

    public async Task<Policy> UpdatePolicyAsync(Guid policyId, string? name, string? description, PolicyEffect? effect, string[]? permissions, string? condition, bool? isActive, int? priority, PrincipalType? principalType, string? principalId, string? resourceType, CancellationToken ct = default)
    {
        var policy = await policyRepo.GetByIdAsync(policyId, ct) ?? throw new KeyNotFoundException("Politique introuvable.");

        policy.Update(name, description, effect, permissions, condition, isActive);
        policy.SetTarget(principalType ?? policy.PrincipalType,
                         principalId ?? policy.PrincipalId,
                         resourceType ?? policy.ResourceType,
                         priority ?? policy.Priority);

        policyRepo.Update(policy);
        await policyRepo.SaveAsync(ct);
        return policy;
    }

    public async Task DeletePolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        var policy = await policyRepo.GetByIdAsync(policyId, ct) ?? throw new KeyNotFoundException("Politique introuvable.");
        policyRepo.SoftDelete(policy);
        await policyRepo.SaveAsync(ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ACL
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<ResourceAcl> CreateAclAsync(Guid tenantId, Guid resourceId, string resourceType, Guid principalId, PrincipalType principalType, string[] permissions, AclEffect effect, string permissionLevel, Guid actorId, CancellationToken ct = default)
    {
        var acl = ResourceAcl.Create(tenantId, resourceId, resourceType, principalType, principalId, permissions, effect, actorId, null);
        await aclRepo.AddAsync(acl, ct);
        await aclRepo.SaveAsync(ct);
        return acl;
    }

    public async Task DeleteAclAsync(Guid aclId, CancellationToken ct = default)
    {
        var acl = await aclRepo.GetByIdAsync(aclId, ct) ?? throw new KeyNotFoundException("Autorisation introuvable.");
        aclRepo.SoftDelete(acl);
        await aclRepo.SaveAsync(ct);
    }
}
