using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;

// ── IAgentRepository ─────────────────────────────────────────────────────────

public interface IAgentRepository
{
    Task<Domain.Agent.Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Domain.Agent.Agent>> SearchAsync(string? q, AgentType? type, AgentStatus? status, AgentVisibility? visibility, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(Domain.Agent.Agent agent, CancellationToken ct = default);
    void Update(Domain.Agent.Agent agent);
    void SoftDelete(Domain.Agent.Agent agent);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class AgentRepository(EaiosDbContext db) : RepositoryBase<Domain.Agent.Agent>(db), IAgentRepository
{
    public async Task<PagedResult<Domain.Agent.Agent>> SearchAsync(string? q, AgentType? type, AgentStatus? status, AgentVisibility? visibility, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(a => a.Name.Contains(q) || (a.DisplayName != null && a.DisplayName.Contains(q)));
        if (type.HasValue)       query = query.Where(a => a.Type       == type);
        if (status.HasValue)     query = query.Where(a => a.Status     == status);
        if (visibility.HasValue) query = query.Where(a => a.Visibility == visibility);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Domain.Agent.Agent>(items, page, pageSize, total);
    }
}

// ── IAgentVersionRepository ──────────────────────────────────────────────────

public interface IAgentVersionRepository
{
    Task<AgentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AgentVersion?> FindAsync(Guid agentId, int versionNumber, CancellationToken ct = default);
    Task<AgentVersion?> FindLatestAsync(Guid agentId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentVersion>> ListAsync(Guid agentId, CancellationToken ct = default);
    Task AddAsync(AgentVersion version, CancellationToken ct = default);
    Task<int> SaveAsync(CancellationToken ct = default);
}

/// <summary>
/// Les versions sont des instantanés immuables : ce dépôt n'expose ni
/// <c>Update</c> ni <c>SoftDelete</c>. Une exécution en cours doit pouvoir
/// retrouver, des jours plus tard, exactement la configuration sur laquelle elle
/// s'était arrêtée.
/// </summary>
public sealed class AgentVersionRepository(EaiosDbContext db)
    : RepositoryBase<AgentVersion>(db), IAgentVersionRepository
{
    public async Task<AgentVersion?> FindAsync(Guid agentId, int versionNumber, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .FirstOrDefaultAsync(v => v.AgentId == agentId && v.VersionNumber == versionNumber, ct);

    public async Task<AgentVersion?> FindLatestAsync(Guid agentId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(v => v.AgentId == agentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AgentVersion>> ListAsync(Guid agentId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(v => v.AgentId == agentId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
}

// ── IAgentExecutionRepository ────────────────────────────────────────────────

public interface IAgentExecutionRepository
{
    Task<AgentExecution?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<AgentExecution>> GetByAgentAsync(Guid agentId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<AgentExecution>> GetByUserAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Les tours d'un fil, du premier au dernier.</summary>
    Task<IReadOnlyList<AgentExecution>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default);
    Task AddAsync(AgentExecution execution, CancellationToken ct = default);
    void Update(AgentExecution execution);
    void SoftDelete(AgentExecution execution);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class AgentExecutionRepository(EaiosDbContext db) : RepositoryBase<AgentExecution>(db), IAgentExecutionRepository
{
    public async Task<PagedResult<AgentExecution>> GetByAgentAsync(Guid agentId, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.Where(e => e.AgentId == agentId).OrderByDescending(e => e.StartedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AgentExecution>(items, page, pageSize, total);
    }

    public async Task<PagedResult<AgentExecution>> GetByUserAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.Where(e => e.UserId == userId).OrderByDescending(e => e.StartedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AgentExecution>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<AgentExecution>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        await Set.Where(e => e.SessionId == sessionId).OrderBy(e => e.StartedAt).ToListAsync(ct);
}

// ── IAgentConversationRepository ─────────────────────────────────────────────

public interface IAgentConversationRepository
{
    Task<AgentConversation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Les fils d'une personne, épinglés d'abord puis du plus récent au plus ancien.</summary>
    Task<PagedResult<AgentConversation>> ListForUserAsync(Guid userId, Guid? agentId, string? q, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(AgentConversation conversation, CancellationToken ct = default);
    void Update(AgentConversation conversation);
    void SoftDelete(AgentConversation conversation);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class AgentConversationRepository(EaiosDbContext db)
    : RepositoryBase<AgentConversation>(db), IAgentConversationRepository
{
    public async Task<PagedResult<AgentConversation>> ListForUserAsync(Guid userId, Guid? agentId, string? q, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().Where(c => c.UserId == userId);
        if (agentId.HasValue) query = query.Where(c => c.AgentId == agentId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Title.Contains(q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.LastActivityAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<AgentConversation>(items, page, pageSize, total);
    }
}

// ── IAgentMemoryRepository ───────────────────────────────────────────────────

public interface IAgentMemoryRepository
{
    Task<AgentMemory?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AgentMemory?> FindByKeyAsync(Guid agentId, Guid? userId, AgentMemoryType type, string key, CancellationToken ct = default);
    Task<IReadOnlyList<AgentMemory>> GetByAgentAsync(Guid agentId, Guid? userId, AgentMemoryType? type, CancellationToken ct = default);
    Task AddAsync(AgentMemory memory, CancellationToken ct = default);
    void Update(AgentMemory memory);
    void SoftDelete(AgentMemory memory);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class AgentMemoryRepository(EaiosDbContext db) : RepositoryBase<AgentMemory>(db), IAgentMemoryRepository
{
    public async Task<AgentMemory?> FindByKeyAsync(Guid agentId, Guid? userId, AgentMemoryType type, string key, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(m => m.AgentId == agentId && m.UserId == userId && m.Type == type && m.Key == key, ct);

    public async Task<IReadOnlyList<AgentMemory>> GetByAgentAsync(Guid agentId, Guid? userId, AgentMemoryType? type, CancellationToken ct = default)
    {
        var q = Set.Where(m => m.AgentId == agentId);
        if (userId.HasValue) q = q.Where(m => m.UserId == userId);
        if (type.HasValue)   q = q.Where(m => m.Type   == type);
        return await q.OrderByDescending(m => m.ImportanceScore).ToListAsync(ct);
    }
}
