using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Identity;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum UserStatus { PendingVerification, Active, Suspended, Deactivated }
public enum MfaMethod { Totp, Sms, Email, BackupCode }
public enum InvitationStatus { Pending, Accepted, Expired, Revoked }
public enum SessionStatus { Active, Revoked, Expired }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: User
// Table: identity.users
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class User : TenantEntity
{
    // ── Identity ───────────────────────────────────────────────────────────────
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public bool IsEmailVerified { get; private set; }
    public string? EmailVerificationToken { get; private set; }
    public DateTime? EmailVerificationTokenExpiry { get; private set; }

    // ── Profile ────────────────────────────────────────────────────────────────
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? JobTitle { get; private set; }
    public string? Department { get; private set; }
    public string Locale { get; private set; } = "fr";
    public string TimeZone { get; private set; } = "Europe/Paris";

    // ── Security ───────────────────────────────────────────────────────────────
    public string? PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public string? SuspensionReason { get; private set; }
    public bool IsMfaEnabled { get; private set; }
    public string? MfaMethods { get; private set; }           // JSON array
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public string? LastLoginIp { get; private set; }
    public string? PasswordResetToken { get; private set; }
    public DateTime? PasswordResetTokenExpiry { get; private set; }

    // ── Preferences ────────────────────────────────────────────────────────────
    public string? NotificationPreferences { get; private set; } // JSONB

    // ── External Auth ──────────────────────────────────────────────────────────
    public string? ExternalProvider { get; private set; }
    public string? ExternalProviderId { get; private set; }

    // ── Quotas ─────────────────────────────────────────────────────────────────
    public long StorageUsedBytes { get; private set; }
    public int MonthlyTokensUsed { get; private set; }

    // ── Mutators ───────────────────────────────────────────────────────────────
    public static User Create(Guid organizationId, string email, string firstName, string lastName)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim().ToLowerInvariant(),
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Status = UserStatus.PendingVerification
        };
        user.SetOrganizationId(organizationId);
        user.SetCreated(null);
        return user;
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void SetEmailVerificationToken(string token, int expiryHours = 24)
    {
        EmailVerificationToken = token;
        EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(expiryHours);
    }

    public bool VerifyEmail(string token)
    {
        if (EmailVerificationToken != token || EmailVerificationTokenExpiry < DateTime.UtcNow)
            return false;
        IsEmailVerified = true;
        Status = UserStatus.Active;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiry = null;
        return true;
    }

    public void RecordLogin(string ip)
    {
        LastLoginAt = DateTime.UtcNow;
        LastLoginIp = ip;
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    public void RecordFailedLogin(int maxAttempts, int lockoutMinutes)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
            LockedUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
    }

    public bool IsLockedOut => LockedUntil.HasValue && LockedUntil > DateTime.UtcNow;

    public void Suspend(string reason) { Status = UserStatus.Suspended; SuspensionReason = reason; }
    public void Activate() { Status = UserStatus.Active; SuspensionReason = null; LockedUntil = null; FailedLoginAttempts = 0; }

    public void EnableMfa(MfaMethod method)
    {
        IsMfaEnabled = true;
        // Simplified: store as CSV for in-memory compat
        var methods = (MfaMethods ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!methods.Contains(method.ToString()))
            methods.Add(method.ToString());
        MfaMethods = string.Join(',', methods);
    }

    public void DisableMfa(MfaMethod method)
    {
        var methods = (MfaMethods ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        methods.Remove(method.ToString());
        MfaMethods = string.Join(',', methods);
        if (methods.Count == 0) IsMfaEnabled = false;
    }

    public void SetPasswordResetToken(string token, int expiryHours = 1)
    {
        PasswordResetToken = token;
        PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(expiryHours);
    }

    public bool ValidatePasswordResetToken(string token) =>
        PasswordResetToken == token && PasswordResetTokenExpiry > DateTime.UtcNow;

    public void ClearPasswordResetToken()
    {
        PasswordResetToken = null;
        PasswordResetTokenExpiry = null;
    }

    public void UpdateProfile(string? firstName, string? lastName, string? jobTitle, string? locale, string? timeZone, string? notificationPrefs)
    {
        if (!string.IsNullOrWhiteSpace(firstName)) FirstName = firstName.Trim();
        if (!string.IsNullOrWhiteSpace(lastName)) LastName = lastName.Trim();
        if (jobTitle is not null) JobTitle = jobTitle;
        if (!string.IsNullOrWhiteSpace(locale)) Locale = locale;
        if (!string.IsNullOrWhiteSpace(timeZone)) TimeZone = timeZone;
        if (notificationPrefs is not null) NotificationPreferences = notificationPrefs;
    }

    public void SetAvatarUrl(string url) => AvatarUrl = url;
    public string FullName => $"{FirstName} {LastName}".Trim();
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Session
// Table: identity.sessions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Session : TenantEntity
{
    public Guid UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    public string? AccessTokenJti { get; private set; }   // JWT ID for revocation
    public SessionStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? DeviceFingerprint { get; private set; }
    public DateTime? LastActivityAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public static Session Create(Guid organizationId, Guid userId, string refreshTokenHash, int lifetimeDays,
        string? ip = null, string? userAgent = null)
    {
        var session = new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            RefreshTokenHash = refreshTokenHash,
            Status = SessionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(lifetimeDays),
            IpAddress = ip,
            UserAgent = userAgent,
            LastActivityAt = DateTime.UtcNow
        };
        session.SetOrganizationId(organizationId);
        session.SetCreated(userId);
        return session;
    }

    public void RotateRefreshToken(string newHash) => RefreshTokenHash = newHash;
    public void RecordActivity() => LastActivityAt = DateTime.UtcNow;
    public bool IsValid => Status == SessionStatus.Active && ExpiresAt > DateTime.UtcNow;

    public void Revoke(string reason)
    {
        Status = SessionStatus.Revoked;
        RevokedAt = DateTime.UtcNow;
        RevocationReason = reason;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: MfaCredential
// Table: identity.mfa_credentials
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class MfaCredential : TenantEntity
{
    public Guid UserId { get; private set; }
    public MfaMethod Method { get; private set; }
    public string SecretEncrypted { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public string? BackupCodesJson { get; private set; } // JSON array of hashed backup codes
    public DateTime? LastUsedAt { get; private set; }

    public static MfaCredential Create(Guid organizationId, Guid userId, MfaMethod method, string secretEncrypted)
    {
        var cred = new MfaCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Method = method,
            SecretEncrypted = secretEncrypted,
            IsActive = false
        };
        cred.SetOrganizationId(organizationId);
        cred.SetCreated(userId);
        return cred;
    }

    public void Activate(string? backupCodesJson = null) { IsActive = true; BackupCodesJson = backupCodesJson; }
    public void Deactivate() => IsActive = false;
    public void RecordUse() => LastUsedAt = DateTime.UtcNow;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: ApiKey
// Table: identity.api_keys
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class ApiKey : TenantEntity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;  // First 8 chars for display
    public string KeyHash { get; private set; } = string.Empty;    // SHA-256 of full key
    public string[] Scopes { get; private set; } = [];
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public string? LastUsedIp { get; private set; }
    public bool IsActive { get; private set; }

    public static ApiKey Create(Guid organizationId, Guid userId, string name, string keyPrefix,
        string keyHash, string[]? scopes = null, DateTime? expiresAt = null)
    {
        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Scopes = scopes ?? [],
            ExpiresAt = expiresAt,
            IsActive = true
        };
        key.SetOrganizationId(organizationId);
        key.SetCreated(userId);
        return key;
    }

    public bool IsValid => IsActive && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
    public void RecordUse(string ip) { LastUsedAt = DateTime.UtcNow; LastUsedIp = ip; }
    public void Revoke() => IsActive = false;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Invitation
// Table: identity.invitations
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Invitation : TenantEntity
{
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public Guid? RoleId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public InvitationStatus Status { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public Guid InvitedBy { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public string? PersonalMessage { get; private set; }
    public int ResendCount { get; private set; }
    public DateTime? LastSentAt { get; private set; }

    public static Invitation Create(Guid organizationId, string email, Guid invitedBy,
        Guid? roleId = null, Guid? workspaceId = null, string? message = null)
    {
        var inv = new Invitation
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim().ToLowerInvariant(),
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            RoleId = roleId,
            WorkspaceId = workspaceId,
            Status = InvitationStatus.Pending,
            Token = Guid.CreateVersion7().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            InvitedBy = invitedBy,
            PersonalMessage = message,
            LastSentAt = DateTime.UtcNow
        };
        inv.SetOrganizationId(organizationId);
        inv.SetCreated(invitedBy);
        return inv;
    }

    public bool IsValid => Status == InvitationStatus.Pending && ExpiresAt > DateTime.UtcNow;
    public void Accept(Guid userId) { Status = InvitationStatus.Accepted; AcceptedAt = DateTime.UtcNow; AcceptedByUserId = userId; }
    public void Revoke() => Status = InvitationStatus.Revoked;
    public void Resend() { ResendCount++; LastSentAt = DateTime.UtcNow; ExpiresAt = DateTime.UtcNow.AddDays(7); }
}
