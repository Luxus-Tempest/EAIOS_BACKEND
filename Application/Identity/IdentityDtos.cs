using EAIOS.Api.Domain.Identity;

namespace EAIOS.Api.Application.Identity;

// ── Login ─────────────────────────────────────────────────────────────────────

public sealed record LoginRequest(
    string  Email,
    string  Password,
    string? MfaCode   = null,
    string? MfaToken  = null);

public sealed record LoginResponse(
    string?  AccessToken,
    string?  RefreshToken,
    int      ExpiresIn,
    UserDto  User,
    bool     RequiresMfa = false,
    string?  MfaToken    = null);

// ── Refresh ───────────────────────────────────────────────────────────────────

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record RefreshResponse(
    string AccessToken,
    string RefreshToken,
    int    ExpiresIn);

// ── Logout ────────────────────────────────────────────────────────────────────

public sealed record LogoutRequest(string RefreshToken);

// ── Register ──────────────────────────────────────────────────────────────────

public sealed record RegisterRequest(
    string InvitationToken,
    string Email,
    string FirstName,
    string LastName,
    string Password);

// ── MFA ───────────────────────────────────────────────────────────────────────

public sealed record MfaSetupDto(
    string   Secret,
    string   QrCodeUri,
    string[] BackupCodes);

public sealed record EnableTotpRequest(
    string   Secret,
    string   Code,
    string[] BackupCodes);

public sealed record DisableMfaRequest(string Password);

// ── User DTO ──────────────────────────────────────────────────────────────────

public sealed record UserDto(
    Guid           Id,
    Guid           OrganizationId,
    string         Email,
    string         FirstName,
    string         LastName,
    string         FullName,
    string         DisplayName,
    string?        AvatarUrl,
    string?        JobTitle,
    string?        Department,
    string         Locale,
    string         TimeZone,
    UserStatus     Status,
    bool           IsEmailVerified,
    bool           IsMfaEnabled,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset  CreatedAt,
    string[]       Roles,
    string[]       Permissions);

// ── Session DTO ───────────────────────────────────────────────────────────────

public sealed record SessionDto(
    Guid            Id,
    string?         DeviceType,
    string?         DeviceOs,
    string?         DeviceName,
    string?         IpAddress,
    SessionStatus   Status,
    DateTimeOffset  LastActivityAt,
    DateTimeOffset  CreatedAt,
    DateTimeOffset  ExpiresAt);

// ── ApiKey DTOs ───────────────────────────────────────────────────────────────

public sealed record ApiKeyDto(
    Guid            Id,
    string          Name,
    string          KeyPrefix,
    bool            IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset  CreatedAt);

public sealed record ApiKeyCreatedDto(
    Guid           Id,
    string         Name,
    string         FullKey,   // Retourné UNE SEULE FOIS
    string         KeyPrefix,
    DateTimeOffset CreatedAt);

public sealed record CreateApiKeyRequest(
    string          Name,
    DateTimeOffset? ExpiresAt = null,
    string[]?       Scopes    = null);
