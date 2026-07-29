using EAIOS.Api.Domain.Identity;

namespace EAIOS.Api.Application.Identity;

// ── Auth Requests ─────────────────────────────────────────────────────────────

public sealed record LoginRequest(string Email, string Password, string? MfaCode = null, bool RememberMe = false);
public sealed record MfaLoginRequest(string MfaToken, string Code, string Method = "totp");
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record LogoutRequest(string? RefreshToken = null);
public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName, string InvitationToken);
public sealed record VerifyEmailRequest(string Token, string Email);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record EnableTotpRequest(string Code);
public sealed record DisableMfaRequest(string Password);

// ── Auth Responses ────────────────────────────────────────────────────────────

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    UserDto User,
    bool RequiresMfa = false,
    string? MfaToken = null);

public sealed record MfaSetupDto(
    string Secret,
    string QrCodeDataUri,
    string[] BackupCodes);

// ── User DTOs ─────────────────────────────────────────────────────────────────

public sealed record UserDto(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? DisplayName,
    string? AvatarUrl,
    string? JobTitle,
    string? Department,
    string Locale,
    string TimeZone,
    UserStatus Status,
    bool IsEmailVerified,
    bool IsMfaEnabled,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    UserStatus Status,
    DateTime? LastLoginAt);

public sealed record SessionDto(
    Guid Id,
    string? IpAddress,
    string? UserAgent,
    DateTime? LastActivityAt,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    bool IsCurrent);

// ── Profile ───────────────────────────────────────────────────────────────────

public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? PhoneNumber,
    string? Locale,
    string? TimeZone,
    string? NotificationPreferences);

// ── API Keys ──────────────────────────────────────────────────────────────────

public sealed record CreateApiKeyRequest(string Name, string[]? Scopes = null, DateTime? ExpiresAt = null);

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    string[] Scopes,
    bool IsActive,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime CreatedAt);

public sealed record ApiKeyCreatedDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    string FullKey,        // Returned ONCE only at creation
    string[] Scopes,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

// ── Invitations ───────────────────────────────────────────────────────────────

public sealed record CreateInvitationRequest(
    string Email,
    string? FirstName = null,
    string? LastName = null,
    Guid? RoleId = null,
    Guid? WorkspaceId = null,
    Guid? DepartmentId = null,
    string? PersonalMessage = null);

public sealed record InvitationDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    InvitationStatus Status,
    Guid InvitedBy,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    int ResendCount,
    DateTime? LastSentAt);
