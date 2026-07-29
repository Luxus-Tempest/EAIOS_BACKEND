using EAIOS.Api.Domain.AccessControl;

namespace EAIOS.Api.Application.AccessControl;

// ── Role ──────────────────────────────────────────────────────────────────────

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    RoleScope Scope,
    bool IsSystem,
    bool IsDefault,
    string[] PermissionCodes,
    string? Color,
    int UserCount,
    DateTime CreatedAt);

public sealed record CreateRoleRequest(
    string Name,
    string? Description = null,
    RoleScope Scope = RoleScope.Organization,
    string? Color = null,
    string[]? PermissionCodes = null);

public sealed record UpdateRoleRequest(
    string? DisplayName,
    string? Description,
    string? Color);

public sealed record SetRolePermissionsRequest(string[] PermissionCodes);

// ── Permission ────────────────────────────────────────────────────────────────

public sealed record PermissionDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Module,
    bool IsSystem);

public sealed record PermissionCatalogDto(
    string Module,
    IReadOnlyList<PermissionDto> Permissions);

// ── User Role Assignment ──────────────────────────────────────────────────────

public sealed record UserRoleDto(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid? WorkspaceId,
    Guid? DepartmentId,
    DateTime? ExpiresAt,
    Guid AssignedBy,
    DateTime AssignedAt);

public sealed record AssignRoleRequest(
    Guid RoleId,
    Guid? WorkspaceId = null,
    Guid? DepartmentId = null,
    DateTime? ExpiresAt = null);

// ── Policy (ABAC) ─────────────────────────────────────────────────────────────

public sealed record PolicyDto(
    Guid Id,
    string Name,
    string? Description,
    PolicyType Type,
    PolicyEffect Effect,
    PrincipalType PrincipalType,
    string? PrincipalId,
    string[] Permissions,
    string? ResourceType,
    string? Condition,
    bool IsActive,
    int Priority,
    DateTime CreatedAt);

public sealed record CreatePolicyRequest(
    string Name,
    PolicyType Type,
    PolicyEffect Effect,
    PrincipalType PrincipalType,
    string[] Permissions,
    string? PrincipalId = null,
    string? ResourceType = null,
    string? Condition = null,
    string? Description = null);

public sealed record UpdatePolicyRequest(
    string? Name,
    string? Description,
    PolicyEffect? Effect,
    string[]? Permissions,
    string? Condition,
    bool? IsActive);

// ── Resource ACL ──────────────────────────────────────────────────────────────

public sealed record ResourceAclDto(
    Guid Id,
    Guid ResourceId,
    string ResourceType,
    PrincipalType PrincipalType,
    Guid? PrincipalId,
    string[] Permissions,
    AclEffect Effect,
    DateTime? ExpiresAt,
    Guid GrantedBy,
    DateTime CreatedAt);

public sealed record CreateAclRequest(
    PrincipalType PrincipalType,
    Guid? PrincipalId,
    string[] Permissions,
    AclEffect Effect = AclEffect.Allow,
    DateTime? ExpiresAt = null);

// ── Access Check ──────────────────────────────────────────────────────────────

public sealed record AccessCheckRequest(
    string Permission,
    Guid? ResourceId = null,
    string? ResourceType = null,
    Guid? ContextWorkspaceId = null);

public sealed record AccessCheckResult(
    bool Allowed,
    string Permission,
    string EvaluatedBy,
    string? DenyReason = null);
