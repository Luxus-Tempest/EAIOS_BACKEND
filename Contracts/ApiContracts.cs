namespace EAIOS.Api.Contracts;

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false, string? DeviceId = null);
public sealed record RefreshRequest(string RefreshToken);
public sealed record BootstrapOrganizationRequest(string OrganizationName, string Email, string FirstName, string LastName, string Password);
public sealed record UpdateOrganizationRequest(string? Name, string? DefaultLanguage, string? TimeZone);
public sealed record CreateWorkspaceRequest(string Name, string? Description, string Type = "Project", string? IconCode = null, string? Color = null);
public sealed record UpdateWorkspaceRequest(string Name, string? Description, string? Type = null);
public sealed record AddMemberRequest(Guid UserId, string Type = "Member");
public sealed record CreateDepartmentRequest(string Name, string Code, Guid? WorkspaceId = null, Guid? ManagerId = null);
public sealed record UpdateDepartmentRequest(string Name, string Code, Guid? WorkspaceId = null, Guid? ManagerId = null);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt, string TokenType, object User);
