using EAIOS.Api.Domain.Organization;

namespace EAIOS.Api.Application.Organization;

// ── Organization ──────────────────────────────────────────────────────────────

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    string? DisplayName,
    string? Description,
    string? LogoUrl,
    OrganizationStatus Status,
    string DefaultLanguage,
    string TimeZone,
    string? Industry,
    int EmployeeCount,
    long StorageQuotaBytes,
    long StorageUsedBytes,
    int MaxUsers,
    int CurrentUsers,
    string PlanId,
    DateTime? TrialEndsAt,
    bool MfaRequired,
    bool SsoEnabled,
    DateTime CreatedAt);

public sealed record UpdateOrganizationRequest(
    string? Name,
    string? DisplayName,
    string? Description,
    string? DefaultLanguage,
    string? TimeZone,
    string? Industry,
    bool? MfaRequired);

// ── Workspace ────────────────────────────────────────────────────────────────

public sealed record WorkspaceDto(
    Guid Id,
    string Name,
    string? Description,
    WorkspaceType Type,
    WorkspaceStatus Status,
    string? Color,
    string? IconCode,
    Guid OwnerId,
    int MemberCount,
    long StorageUsedBytes,
    string[] Tags,
    DateTime CreatedAt);

public sealed record CreateWorkspaceRequest(
    string Name,
    string? Description = null,
    WorkspaceType Type = WorkspaceType.Standard,
    string? Color = null,
    string? IconCode = null,
    Guid? DepartmentId = null);

public sealed record UpdateWorkspaceRequest(
    string? Name,
    string? Description,
    string? Color,
    string? IconCode);

// ── Department ───────────────────────────────────────────────────────────────

public sealed record DepartmentDto(
    Guid Id,
    string Name,
    string? Description,
    string? Code,
    DepartmentStatus Status,
    Guid? ParentId,
    Guid? ManagerId,
    string? Color,
    string? IconCode,
    int MemberCount,
    DateTime CreatedAt);

public sealed record CreateDepartmentRequest(
    string Name,
    string? Description = null,
    string? Code = null,
    Guid? ParentId = null,
    Guid? ManagerId = null);

public sealed record UpdateDepartmentRequest(
    string? Name,
    string? Description,
    string? Code,
    Guid? ManagerId);

// ── Members ──────────────────────────────────────────────────────────────────

public sealed record MemberDto(
    Guid UserId,
    string Email,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    MembershipType MembershipType,
    MembershipStatus MembershipStatus,
    DateTime JoinedAt);

public sealed record AddMemberRequest(
    Guid UserId,
    MembershipType Type = MembershipType.Member);
