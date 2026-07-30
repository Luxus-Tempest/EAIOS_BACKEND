using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Moteur d'évaluation des permissions à 3 couches :
///   1. Super-admin bypass (platform.admin)
///   2. RBAC — rôles de l'utilisateur
///   3. ABAC — politiques basées sur attributs (deny-overrides-allow)
/// </summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);
    Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default);
    Task<string[]> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class PermissionService(
    IRoleRepository roleRepository,
    IPolicyRepository policyRepository,
    ICurrentUser currentUser) : IPermissionService
{
    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        // Couche 0 : Super-admin bypass
        if (currentUser.IsPlatformAdmin) return true;

        // Couche 1 : RBAC
        var permissions = await GetEffectivePermissionsAsync(userId, ct);
        if (permissions.Contains(permission) || permissions.Contains("*"))
            return true;

        // Couche 2 : ABAC
        var policies = await policyRepository.GetActiveAsync(ct);

        var denyMatches = policies.Where(p =>
            p.Effect == PolicyEffect.Deny &&
            MatchesPrincipal(p, userId) &&
            (p.Permissions.Contains(permission) || p.Permissions.Contains("*")));
        if (denyMatches.Any()) return false;

        var allowMatches = policies.Where(p =>
            p.Effect == PolicyEffect.Allow &&
            MatchesPrincipal(p, userId) &&
            (p.Permissions.Contains(permission) || p.Permissions.Contains("*")));
        return allowMatches.Any();
    }

    public async Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default) =>
        await HasPermissionAsync(userId, permission, ct);

    public async Task<string[]> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var roles = await roleRepository.GetByUserAsync(userId, ct);
        return roles.SelectMany(r => r.PermissionCodes).Distinct().ToArray();
    }

    private static bool MatchesPrincipal(Policy p, Guid userId) =>
        p.PrincipalType == PrincipalType.AllUsers ||
        (p.PrincipalType == PrincipalType.User && p.PrincipalId == userId.ToString());
}
