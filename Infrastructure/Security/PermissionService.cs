using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Moteur d'évaluation des permissions.
///
/// Ordre d'évaluation, du plus fort au plus faible :
///   0. Administrateur plateforme — tout est permis.
///   1. Politiques de <b>refus</b> — un refus l'emporte sur tout ce qui suit,
///      y compris un rôle : c'est ce qui permet de retirer un droit par exception.
///   2. ACL de la ressource — un refus nominatif retire, une autorisation nominative accorde.
///   3. Rôles (RBAC) — l'administrateur d'organisation porte tout par définition.
///   4. Politiques d'<b>autorisation</b>.
///
/// Les politiques portent une condition (<see cref="PolicyConditionEvaluator"/>)
/// évaluée contre la personne, la ressource et l'instant.
/// </summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);

    Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default);

    /// <summary>Même évaluation, avec les attributs de la ressource pour les conditions de politique.</summary>
    Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType,
        IReadOnlyDictionary<string, object?>? resourceAttributes, CancellationToken ct = default);

    Task<string[]> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Ce que la personne peut consulter sans autorisation nominative.</summary>
    Task<ResourceClassification> GetClassificationCeilingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Ce que les ACL nominatives disent d'une ressource pour cette personne.</summary>
    Task<AclDecision> GetAclDecisionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default);

    /// <summary>Les identifiants de ressources explicitement accordés ou refusés à cette personne, pour un type donné.</summary>
    Task<(IReadOnlySet<Guid> Allowed, IReadOnlySet<Guid> Denied)> GetExplicitGrantsAsync(Guid userId, string permission, string resourceType, CancellationToken ct = default);
}

public enum AclDecision { None, Allow, Deny }

public sealed class PermissionService(
    IRoleRepository roleRepository,
    IUserRoleRepository userRoleRepository,
    IPolicyRepository policyRepository,
    IResourceAclRepository aclRepository,
    ICurrentUser currentUser,
    ILogger<PermissionService> logger) : IPermissionService
{
    public const string Wildcard = "*";

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        if (currentUser.IsPlatformAdmin) return true;

        var policies = await policyRepository.GetActiveAsync(ct);
        var subject  = await LoadSubjectAsync(userId, ct);
        var attrs    = subject.Attributes(permission, null);

        if (policies.Any(p => p.Effect == PolicyEffect.Deny && Applies(p, subject, permission, null, attrs)))
            return false;

        if (subject.Permissions.Contains(permission) || subject.Permissions.Contains(Wildcard))
            return true;

        return policies.Any(p => p.Effect == PolicyEffect.Allow && Applies(p, subject, permission, null, attrs));
    }

    public Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default) =>
        HasResourcePermissionAsync(userId, permission, resourceId, resourceType, null, ct);

    public async Task<bool> HasResourcePermissionAsync(Guid userId, string permission, Guid resourceId, string resourceType,
        IReadOnlyDictionary<string, object?>? resourceAttributes, CancellationToken ct = default)
    {
        if (currentUser.IsPlatformAdmin) return true;

        var policies = await policyRepository.GetActiveAsync(ct);
        var subject  = await LoadSubjectAsync(userId, ct);
        var attrs    = subject.Attributes(permission, resourceAttributes, resourceId, resourceType);

        if (policies.Any(p => p.Effect == PolicyEffect.Deny && Applies(p, subject, permission, resourceType, attrs)))
            return false;

        var decision = AclDecisionFor(await aclRepository.GetByResourceAsync(resourceId, resourceType, ct), subject, permission);
        if (decision == AclDecision.Deny)  return false;
        if (decision == AclDecision.Allow) return true;

        if (subject.Permissions.Contains(permission) || subject.Permissions.Contains(Wildcard))
            return true;

        return policies.Any(p => p.Effect == PolicyEffect.Allow && Applies(p, subject, permission, resourceType, attrs));
    }

    public async Task<string[]> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var subject = await LoadSubjectAsync(userId, ct);
        return subject.Permissions.ToArray();
    }

    public async Task<ResourceClassification> GetClassificationCeilingAsync(Guid userId, CancellationToken ct = default)
    {
        if (currentUser.IsPlatformAdmin) return ResourceClassification.StrictlyConfidential;
        var subject = await LoadSubjectAsync(userId, ct);
        return ClassificationCeiling.For(subject.RoleNames, subject.Permissions);
    }

    public async Task<AclDecision> GetAclDecisionAsync(Guid userId, string permission, Guid resourceId, string resourceType, CancellationToken ct = default)
    {
        var subject = await LoadSubjectAsync(userId, ct);
        return AclDecisionFor(await aclRepository.GetByResourceAsync(resourceId, resourceType, ct), subject, permission);
    }

    public async Task<(IReadOnlySet<Guid> Allowed, IReadOnlySet<Guid> Denied)> GetExplicitGrantsAsync(Guid userId, string permission, string resourceType, CancellationToken ct = default)
    {
        var subject = await LoadSubjectAsync(userId, ct);
        var now = DateTime.UtcNow;

        // Les ACL adressées à la personne, à ses rôles, à ses départements et à
        // ses espaces — chaque bénéficiaire possible de ce qu'elle est.
        var rows = new List<ResourceAcl>();
        rows.AddRange(await aclRepository.GetByPrincipalAsync(userId, PrincipalType.User, ct));
        foreach (var roleId in subject.RoleGuids)
            rows.AddRange(await aclRepository.GetByPrincipalAsync(roleId, PrincipalType.Role, ct));
        foreach (var departmentId in subject.DepartmentIds)
            rows.AddRange(await aclRepository.GetByPrincipalAsync(departmentId, PrincipalType.Department, ct));
        foreach (var workspaceId in subject.WorkspaceIds)
            rows.AddRange(await aclRepository.GetByPrincipalAsync(workspaceId, PrincipalType.Workspace, ct));

        var relevant = rows.Where(a =>
            string.Equals(a.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase)
            && (a.ExpiresAt == null || a.ExpiresAt > now)
            && (a.Permissions.Contains(permission) || a.Permissions.Contains(Wildcard))).ToList();

        var denied  = relevant.Where(a => a.Effect == AclEffect.Deny).Select(a => a.ResourceId).ToHashSet();
        var allowed = relevant.Where(a => a.Effect == AclEffect.Allow).Select(a => a.ResourceId).Where(id => !denied.Contains(id)).ToHashSet();
        return (allowed, denied);
    }

    // ── Correspondance ────────────────────────────────────────────────────────

    private bool Applies(Policy p, Subject subject, string permission, string? resourceType, IReadOnlyDictionary<string, object?> attrs)
    {
        if (!p.Permissions.Contains(permission) && !p.Permissions.Contains(Wildcard)) return false;
        if (resourceType is not null && p.ResourceType is not null
            && !string.Equals(p.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase)) return false;
        if (!MatchesPrincipal(p.PrincipalType, p.PrincipalId, subject)) return false;

        if (string.IsNullOrWhiteSpace(p.Condition)) return true;

        if (!PolicyConditionEvaluator.TryEvaluate(p.Condition, attrs, out var holds, out var error))
        {
            // Une condition illisible ne s'applique pas — et se voit dans le journal
            // plutôt que d'accorder ou de refuser au hasard.
            logger.LogWarning("Politique {PolicyId} « {Name} » : condition illisible ({Error}).", p.Id, p.Name, error);
            return false;
        }
        return holds;
    }

    private static AclDecision AclDecisionFor(IReadOnlyList<ResourceAcl> acls, Subject subject, string permission)
    {
        var now = DateTime.UtcNow;
        var live = acls.Where(a => (a.ExpiresAt == null || a.ExpiresAt > now)
                                && MatchesPrincipal(a.PrincipalType, a.PrincipalId?.ToString(), subject)
                                && (a.Permissions.Contains(permission) || a.Permissions.Contains(Wildcard)))
                       .ToList();

        if (live.Any(a => a.Effect == AclEffect.Deny)) return AclDecision.Deny;
        if (live.Any(a => a.Effect == AclEffect.Allow)) return AclDecision.Allow;
        return AclDecision.None;
    }

    /// <summary>
    /// Les cinq types de bénéficiaires sont reconnus. Un département ou un espace
    /// se compare aux attributions de rôle portant cette portée.
    /// </summary>
    private static bool MatchesPrincipal(PrincipalType type, string? principalId, Subject subject) => type switch
    {
        PrincipalType.All        => true,
        PrincipalType.User       => Guid.TryParse(principalId, out var uid) && uid == subject.UserId,
        PrincipalType.Role       => principalId is not null &&
                                    (subject.RoleIds.Contains(principalId) ||
                                     subject.RoleNames.Contains(principalId, StringComparer.OrdinalIgnoreCase)),
        PrincipalType.Department => Guid.TryParse(principalId, out var did) && subject.DepartmentIds.Contains(did),
        PrincipalType.Workspace  => Guid.TryParse(principalId, out var wid) && subject.WorkspaceIds.Contains(wid),
        _                        => false
    };

    private async Task<Subject> LoadSubjectAsync(Guid userId, CancellationToken ct)
    {
        var roles       = await roleRepository.GetByUserAsync(userId, ct);
        var assignments = await userRoleRepository.GetByUserAsync(userId, ct);
        return new Subject(userId, currentUser.Email, roles, assignments);
    }

    /// <summary>Ce que l'on sait de la personne au moment de décider.</summary>
    private sealed class Subject
    {
        public Guid UserId { get; }
        public string? Email { get; }
        public HashSet<string> Permissions { get; }
        public HashSet<string> RoleIds { get; }
        public HashSet<Guid> RoleGuids { get; }
        public HashSet<string> RoleNames { get; }
        public HashSet<Guid> DepartmentIds { get; } = [];
        public HashSet<Guid> WorkspaceIds { get; } = [];
        public bool IsOrgAdmin { get; }

        public Subject(Guid userId, string? email, IReadOnlyList<Role> roles, IReadOnlyList<UserRole> assignments)
        {
            UserId      = userId;
            Email       = email;
            RoleIds     = roles.Select(r => r.Id.ToString()).ToHashSet();
            RoleGuids   = roles.Select(r => r.Id).ToHashSet();
            RoleNames   = roles.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Permissions = roles.SelectMany(r => r.PermissionCodes).ToHashSet(StringComparer.Ordinal);
            IsOrgAdmin  = roles.Any(r => r.IsSystem && r.Name == SystemRoles.OrgAdmin);

            // L'administrateur d'organisation a tous les droits dans son
            // organisation, par définition — quelle que soit la liste semée à la
            // création du rôle, qui peut être en retard sur le catalogue.
            if (IsOrgAdmin) Permissions.Add(Wildcard);

            foreach (var a in assignments.Where(a => !a.IsExpired))
            {
                if (a.DepartmentId is { } d) DepartmentIds.Add(d);
                if (a.WorkspaceId  is { } w) WorkspaceIds.Add(w);
            }
        }

        /// <summary>Le sac d'attributs lu par les conditions de politique.</summary>
        public Dictionary<string, object?> Attributes(string permission, IReadOnlyDictionary<string, object?>? resource,
            Guid? resourceId = null, string? resourceType = null)
        {
            var now = DateTime.UtcNow;
            var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["permission"]          = permission,
                ["user.id"]             = UserId.ToString(),
                ["user.email"]          = Email,
                ["user.roles"]          = RoleNames.Cast<object?>().ToList(),
                ["user.permissions"]    = Permissions.Cast<object?>().ToList(),
                ["user.isOrgAdmin"]     = IsOrgAdmin,
                ["user.departmentIds"]  = DepartmentIds.Select(d => (object?)d.ToString()).ToList(),
                ["user.workspaceIds"]   = WorkspaceIds.Select(w => (object?)w.ToString()).ToList(),
                ["time.hour"]           = (double)now.Hour,
                ["time.weekday"]        = now.DayOfWeek.ToString(),
            };
            if (resourceId is { } id)     bag["resource.id"]   = id.ToString();
            if (resourceType is not null) bag["resource.type"] = resourceType;
            if (resource is not null)
                foreach (var (key, value) in resource) bag[key] = value;
            return bag;
        }
    }
}

/// <summary>Le sac d'attributs d'un document, tel que les conditions de politique le lisent.</summary>
public static class ResourceAttributes
{
    public static Dictionary<string, object?> Of(Document d) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["resource.type"]           = "Document",
        ["resource.id"]             = d.Id.ToString(),
        ["resource.classification"] = d.Classification.ToString(),
        ["resource.status"]         = d.Status.ToString(),
        ["resource.ownerId"]        = d.OwnerId.ToString(),
        ["resource.workspaceId"]    = d.WorkspaceId?.ToString(),
        ["resource.departmentId"]   = d.DepartmentId?.ToString(),
        ["resource.folderId"]       = d.FolderId?.ToString(),
        ["resource.mimeType"]       = d.MimeType,
        ["resource.tags"]           = d.Tags.Cast<object?>().ToList(),
        ["resource.hasLegalHold"]   = d.HasLegalHold,
        ["resource.language"]       = d.Language,
    };
}
