using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.AccessControl;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum RoleScope { Organization, Workspace, Department }
public enum PolicyType { Abac, ResourcePolicy }
public enum PolicyEffect { Allow, Deny }
public enum PrincipalType { User, Role, Department, Workspace, All }
public enum AclEffect { Allow, Deny }

// ═══════════════════════════════════════════════════════════════════════════════
// SYSTEM ROLE CONSTANTS
// ═══════════════════════════════════════════════════════════════════════════════

public static class SystemRoles
{
    public const string PlatformOwner  = "platform.owner";
    public const string PlatformAdmin  = "platform.admin";
    public const string OrgAdmin       = "org.admin";
    public const string OrgMember      = "org.member";
    public const string OrgGuest       = "org.guest";
    public const string WorkspaceAdmin = "workspace.admin";
    public const string WorkspaceMember = "workspace.member";
    public const string DeptManager    = "dept.manager";
    public const string DeptMember     = "dept.member";
}

// ═══════════════════════════════════════════════════════════════════════════════
// PERMISSION CATALOG CONSTANTS
// ═══════════════════════════════════════════════════════════════════════════════

public static class Permissions
{
    // Identity
    public const string UserRead    = "user.read";
    public const string UserCreate  = "user.create";
    public const string UserUpdate  = "user.update";
    public const string UserDelete  = "user.delete";
    public const string UserSuspend = "user.suspend";

    // Organization
    public const string OrgRead    = "org.read";
    public const string OrgUpdate  = "org.update";
    public const string OrgManage  = "org.manage";

    // Workspace
    public const string WorkspaceCreate = "workspace.create";
    public const string WorkspaceRead   = "workspace.read";
    public const string WorkspaceUpdate = "workspace.update";
    public const string WorkspaceDelete = "workspace.delete";
    public const string WorkspaceManage = "workspace.manage";

    // Department
    public const string DeptCreate = "department.create";
    public const string DeptRead   = "department.read";
    public const string DeptUpdate = "department.update";
    public const string DeptDelete = "department.delete";

    // Access Control
    public const string RoleCreate     = "role.create";
    public const string RoleRead       = "role.read";
    public const string RoleUpdate     = "role.update";
    public const string RoleDelete     = "role.delete";
    public const string RoleAssign     = "role.assign";
    public const string PolicyCreate   = "policy.create";
    public const string PolicyRead     = "policy.read";
    public const string PolicyUpdate   = "policy.update";

    // Resource
    public const string ResourceCreate   = "resource.create";
    public const string ResourceRead     = "resource.read";
    public const string ResourceUpdate   = "resource.update";
    public const string ResourceDelete   = "resource.delete";
    public const string ResourceShare    = "resource.share";
    public const string ResourceDownload = "resource.download";
    public const string ResourceManage   = "resource.manage";
    public const string LegalHoldManage  = "resource.legal_hold.manage";

    // Knowledge
    public const string KnowledgeItemCreate   = "knowledge.item.create";
    public const string KnowledgeItemRead     = "knowledge.item.read";
    public const string KnowledgeItemUpdate   = "knowledge.item.update";
    public const string KnowledgeItemDelete   = "knowledge.item.delete";
    public const string KnowledgeItemPublish  = "knowledge.item.publish";
    public const string KnowledgeItemValidate = "knowledge.item.validate";
    public const string KnowledgePackCreate   = "knowledge.pack.create";
    public const string KnowledgePackExport   = "knowledge.pack.export";
    public const string KnowledgeGraphRead    = "knowledge.graph.read";
    public const string KnowledgeGraphManage  = "knowledge.graph.manage";

    // Agent
    public const string AgentCreate  = "agent.create";
    public const string AgentRead    = "agent.read";
    public const string AgentUpdate  = "agent.update";
    public const string AgentDelete  = "agent.delete";
    public const string AgentPublish = "agent.publish";
    public const string AgentExecute = "agent.execute";
    public const string AgentMonitor = "agent.monitor";

    // Workflow
    public const string WorkflowCreate  = "workflow.create";
    public const string WorkflowRead    = "workflow.read";
    public const string WorkflowUpdate  = "workflow.update";
    public const string WorkflowDelete  = "workflow.delete";
    public const string WorkflowExecute = "workflow.execute";
    public const string WorkflowManage  = "workflow.manage";

    // Search
    public const string SearchBasicExecute    = "search.basic.execute";
    public const string SearchAdvancedExecute = "search.advanced.execute";
    public const string SearchSave            = "search.save";
    public const string SearchAnalyticsRead   = "search.analytics.read";

    // Analytics
    public const string AnalyticsRead = "analytics.read";

    // Admin
    public const string AdminUsers    = "admin.users";
    public const string AdminOrg      = "admin.org";
    public const string AdminBilling  = "admin.billing";
    public const string AdminPlatform = "admin.platform";
    public const string AdminAudit    = "admin.audit";
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Permission
// Table: org_{id}.access.permissions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Permission : TenantEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Module { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }

    public static Permission Create(Guid organizationId, string code, string name,
        string module, bool isSystem = false, string? description = null)
    {
        var p = new Permission
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = name,
            Module = module,
            IsSystem = isSystem,
            Description = description
        };
        p.SetOrganizationId(organizationId);
        p.SetCreated(null);
        return p;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Role
// Table: org_{id}.access.roles
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Role : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? Description { get; private set; }
    public RoleScope Scope { get; private set; }
    public bool IsSystem { get; private set; }          // Cannot be modified or deleted
    public bool IsDefault { get; private set; }
    public string[] PermissionCodes { get; private set; } = [];  // Denormalized for fast eval
    public string? Color { get; private set; }
    public int UserCount { get; private set; }            // Denormalized

    public static Role Create(Guid organizationId, string name, RoleScope scope,
        bool isSystem = false, string? description = null)
    {
        var r = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim().ToLowerInvariant(),
            DisplayName = name.Trim(),
            Description = description,
            Scope = scope,
            IsSystem = isSystem
        };
        r.SetOrganizationId(organizationId);
        r.SetCreated(null);
        return r;
    }

    public void Update(string? displayName, string? description, string? color)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName;
        if (description is not null) Description = description;
        if (color is not null) Color = color;
    }

    public void SetPermissions(string[] permissionCodes) => PermissionCodes = permissionCodes;
    public bool HasPermission(string code) => PermissionCodes.Contains(code);
    public void IncrementUserCount() => UserCount++;
    public void DecrementUserCount() { if (UserCount > 0) UserCount--; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: UserRole (Assignment)
// Table: org_{id}.access.user_roles
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class UserRole : TenantEntity
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public string RoleName { get; private set; } = string.Empty;  // Denormalized
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public Guid AssignedBy { get; private set; }

    public static UserRole Create(Guid organizationId, Guid userId, Guid roleId, string roleName,
        Guid assignedBy, Guid? workspaceId = null, Guid? departmentId = null, DateTime? expiresAt = null)
    {
        var ur = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            RoleId = roleId,
            RoleName = roleName,
            WorkspaceId = workspaceId,
            DepartmentId = departmentId,
            ExpiresAt = expiresAt,
            AssignedBy = assignedBy
        };
        ur.SetOrganizationId(organizationId);
        ur.SetCreated(assignedBy);
        return ur;
    }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Policy (ABAC)
// Table: org_{id}.access.policies
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Policy : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public PolicyType Type { get; private set; }
    public PolicyEffect Effect { get; private set; }
    public PrincipalType PrincipalType { get; private set; }
    public string? PrincipalId { get; private set; }
    public string[] Permissions { get; private set; } = [];
    public string? ResourceType { get; private set; }
    public string? Condition { get; private set; }  // CEL expression
    public bool IsActive { get; private set; }
    public int Priority { get; private set; }       // Higher = evaluated first

    public static Policy Create(Guid organizationId, string name, PolicyType type,
        PolicyEffect effect, string[] permissions, Guid createdBy)
    {
        var p = new Policy
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Type = type,
            Effect = effect,
            Permissions = permissions,
            IsActive = true,
            Priority = 0
        };
        p.SetOrganizationId(organizationId);
        p.SetCreated(createdBy);
        return p;
    }

    public void Update(string? name, string? description, PolicyEffect? effect, string[]? permissions, string? condition, bool? isActive)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (description is not null) Description = description;
        if (effect.HasValue) Effect = effect.Value;
        if (permissions is not null) Permissions = permissions;
        if (condition is not null) Condition = condition;
        if (isActive.HasValue) IsActive = isActive.Value;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: ResourceAcl (Explicit Resource-Level Permission)
// Table: org_{id}.access.resource_acls
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class ResourceAcl : TenantEntity
{
    public Guid ResourceId { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public PrincipalType PrincipalType { get; private set; }
    public Guid? PrincipalId { get; private set; }
    public string[] Permissions { get; private set; } = [];
    public AclEffect Effect { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public Guid GrantedBy { get; private set; }

    public static ResourceAcl Create(Guid organizationId, Guid resourceId, string resourceType,
        PrincipalType principalType, Guid? principalId, string[] permissions,
        AclEffect effect, Guid grantedBy, DateTime? expiresAt = null)
    {
        var acl = new ResourceAcl
        {
            Id = Guid.CreateVersion7(),
            ResourceId = resourceId,
            ResourceType = resourceType,
            PrincipalType = principalType,
            PrincipalId = principalId,
            Permissions = permissions,
            Effect = effect,
            ExpiresAt = expiresAt,
            GrantedBy = grantedBy
        };
        acl.SetOrganizationId(organizationId);
        acl.SetCreated(grantedBy);
        return acl;
    }
}
