using EAIOS.Api.Domain.Organization;

namespace EAIOS.Api.Application.Organization;

public sealed record CreateWorkspaceRequest(
    string              Name,
    string?             Description = null,
    WorkspaceVisibility Visibility  = WorkspaceVisibility.Private);

public sealed record UpdateWorkspaceRequest(
    string?             Name        = null,
    string?             Description = null,
    string?             AvatarUrl   = null,
    WorkspaceVisibility? Visibility = null);

public sealed record AddMemberRequest(
    Guid   UserId,
    string Role   = "member");

public sealed record CreateDepartmentRequest(
    string  Name,
    string? Code     = null,
    Guid?   ParentId = null,
    Guid?   ManagerId = null);

public sealed record UpdateDepartmentRequest(
    string?  Name        = null,
    string?  Code        = null,
    string?  Description = null,
    Guid?    ManagerId   = null);
