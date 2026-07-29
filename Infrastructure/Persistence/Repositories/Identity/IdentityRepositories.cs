using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;

// ═══════════════════════════════════════════════════════════════════════════════
// USER REPOSITORY
// ═══════════════════════════════════════════════════════════════════════════════

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default);
    Task<PagedResult<User>> SearchAsync(string? search, UserStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
    void SoftDelete(User user);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class UserRepository(EaiosDbContext db) : RepositoryBase<User>(db), IUserRepository
{
    public async Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
        await Set.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task<PagedResult<User>> SearchAsync(string? search, UserStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Email.Contains(search) || u.FirstName.Contains(search) || u.LastName.Contains(search));
        if (status.HasValue)
            query = query.Where(u => u.Status == status.Value);
        return await GetPagedAsync(page, pageSize, orderBy: q => q.OrderBy(u => u.LastName), ct: ct);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// SESSION REPOSITORY
// ═══════════════════════════════════════════════════════════════════════════════

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Session?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
    Task AddAsync(Session session, CancellationToken ct = default);
    void Update(Session session);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class SessionRepository(EaiosDbContext db) : RepositoryBase<Session>(db), ISessionRepository
{
    public async Task<Session?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash && !s.IsDeleted, ct);

    public async Task<IReadOnlyList<Session>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await Set.Where(s => s.UserId == userId && s.Status == SessionStatus.Active)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(ct)).AsReadOnly();

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var sessions = await Set.Where(s => s.UserId == userId && s.Status == SessionStatus.Active).ToListAsync(ct);
        foreach (var s in sessions) { s.Revoke(reason); }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// API KEY REPOSITORY
// ═══════════════════════════════════════════════════════════════════════════════

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiKey?> FindByKeyHashAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(ApiKey apiKey, CancellationToken ct = default);
    void Update(ApiKey apiKey);
    void SoftDelete(ApiKey apiKey);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class ApiKeyRepository(EaiosDbContext db) : RepositoryBase<ApiKey>(db), IApiKeyRepository
{
    public async Task<ApiKey?> FindByKeyHashAsync(string hash, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive, ct);

    public async Task<IReadOnlyList<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await Set.Where(k => k.UserId == userId).OrderByDescending(k => k.CreatedAt).ToListAsync(ct)).AsReadOnly();
}

// ═══════════════════════════════════════════════════════════════════════════════
// INVITATION REPOSITORY
// ═══════════════════════════════════════════════════════════════════════════════

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invitation?> FindByTokenAsync(string token, CancellationToken ct = default);
    Task<Invitation?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<IReadOnlyList<Invitation>> GetAllAsync(InvitationStatus? status, CancellationToken ct = default);
    Task AddAsync(Invitation invitation, CancellationToken ct = default);
    void Update(Invitation invitation);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class InvitationRepository(EaiosDbContext db) : RepositoryBase<Invitation>(db), IInvitationRepository
{
    public async Task<Invitation?> FindByTokenAsync(string token, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task<Invitation?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(i => i.NormalizedEmail == normalizedEmail && i.Status == InvitationStatus.Pending, ct);

    public async Task<IReadOnlyList<Invitation>> GetAllAsync(InvitationStatus? status, CancellationToken ct = default)
    {
        var query = Set.AsQueryable();
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        return (await query.OrderByDescending(i => i.CreatedAt).ToListAsync(ct)).AsReadOnly();
    }
}
