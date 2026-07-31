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

// ── IAgentExecutionRepository ────────────────────────────────────────────────

public interface IAgentExecutionRepository
{
    Task<AgentExecution?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<AgentExecution>> GetByAgentAsync(Guid agentId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<AgentExecution>> GetByUserAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(AgentExecution execution, CancellationToken ct = default);
    void Update(AgentExecution execution);
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
