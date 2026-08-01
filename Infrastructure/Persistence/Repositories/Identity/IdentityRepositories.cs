using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;

// ═══════════════════════════════════════════════════════════════════════════════
// IUserRepository + UserRepository
// ═══════════════════════════════════════════════════════════════════════════════

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<bool>  EmailExistsAsync(string normalizedEmail, CancellationToken ct = default);
    Task<PagedResult<User>> SearchAsync(string? query, UserStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
    void SoftDelete(User user);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class UserRepository(EaiosDbContext db) : RepositoryBase<User>(db), IUserRepository
{
    public async Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        await Set.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted, ct);

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
        await Set.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task<PagedResult<User>> SearchAsync(string? query, UserStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(u => u.Email.Contains(query) || u.FirstName.Contains(query) || u.LastName.Contains(query));
        if (status.HasValue)
            q = q.Where(u => u.Status == status.Value);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<User>(items, page, pageSize, total);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ISessionRepository + SessionRepository
// ═══════════════════════════════════════════════════════════════════════════════

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Session?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
    Task AddAsync(Session session, CancellationToken ct = default);
    void Update(Session session);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class SessionRepository(EaiosDbContext db) : RepositoryBase<Session>(db), ISessionRepository
{
    public async Task<Session?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash && s.Status == SessionStatus.Active, ct);

    public async Task<IReadOnlyList<Session>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        await Set.Where(s => s.UserId == userId && s.Status == SessionStatus.Active)
                 .OrderByDescending(s => s.LastActivityAt)
                 .ToListAsync(ct);

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var sessions = await Set.Where(s => s.UserId == userId && s.Status == SessionStatus.Active).ToListAsync(ct);
        foreach (var s in sessions) s.Revoke(reason);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// IMfaCredentialRepository + MfaCredentialRepository
// ═══════════════════════════════════════════════════════════════════════════════

public interface IMfaCredentialRepository
{
    Task<MfaCredential?> FindByUserAndMethodAsync(Guid userId, MfaMethod method, CancellationToken ct = default);
    Task<IReadOnlyList<MfaCredential>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(MfaCredential cred, CancellationToken ct = default);
    void Update(MfaCredential cred);
    void SoftDelete(MfaCredential cred);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class MfaCredentialRepository(EaiosDbContext db) : RepositoryBase<MfaCredential>(db), IMfaCredentialRepository
{
    public async Task<MfaCredential?> FindByUserAndMethodAsync(Guid userId, MfaMethod method, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(c => c.UserId == userId && c.Method == method && c.IsActive, ct);

    public async Task<IReadOnlyList<MfaCredential>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        await Set.Where(c => c.UserId == userId && c.IsActive).ToListAsync(ct);
}

// ═══════════════════════════════════════════════════════════════════════════════
// IApiKeyRepository + ApiKeyRepository
// ═══════════════════════════════════════════════════════════════════════════════

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiKey?> FindByKeyHashAsync(string keyHash, CancellationToken ct = default);
    Task<IReadOnlyList<ApiKey>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(ApiKey apiKey, CancellationToken ct = default);
    void Update(ApiKey apiKey);
    void SoftDelete(ApiKey apiKey);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class ApiKeyRepository(EaiosDbContext db) : RepositoryBase<ApiKey>(db), IApiKeyRepository
{
    public async Task<ApiKey?> FindByKeyHashAsync(string keyHash, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive, ct);

    public async Task<IReadOnlyList<ApiKey>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
        await Set.Where(k => k.UserId == userId)
                 .OrderByDescending(k => k.CreatedAt)
                 .ToListAsync(ct);
}

// ═══════════════════════════════════════════════════════════════════════════════
// IInvitationRepository + InvitationRepository
// ═══════════════════════════════════════════════════════════════════════════════

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invitation?> FindByTokenAsync(string token, CancellationToken ct = default);
    Task<Invitation?> FindPendingByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<IReadOnlyList<Invitation>> ListAsync(InvitationStatus? status, CancellationToken ct = default);
    Task AddAsync(Invitation invitation, CancellationToken ct = default);
    void Update(Invitation invitation);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class InvitationRepository(EaiosDbContext db) : RepositoryBase<Invitation>(db), IInvitationRepository
{
    public async Task<Invitation?> FindByTokenAsync(string token, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task<Invitation?> FindPendingByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(i => i.NormalizedEmail == normalizedEmail && i.Status == InvitationStatus.Pending, ct);

    public async Task<IReadOnlyList<Invitation>> ListAsync(InvitationStatus? status, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(i => i.Status == status.Value);
        return await q.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
    }
}
