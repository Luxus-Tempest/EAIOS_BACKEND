using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Workflow;

// ── IWorkflowDefinitionRepository ───────────────────────────────────────────

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetWithVersionsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<WorkflowDefinition>> SearchAsync(string? q, WorkflowDefinitionStatus? status, string? category, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(WorkflowDefinition definition, CancellationToken ct = default);
    void Update(WorkflowDefinition definition);
    void SoftDelete(WorkflowDefinition definition);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class WorkflowDefinitionRepository(EaiosDbContext db) : RepositoryBase<WorkflowDefinition>(db), IWorkflowDefinitionRepository
{
    public async Task<WorkflowDefinition?> GetWithVersionsAsync(Guid id, CancellationToken ct = default) =>
        await Set.Include(w => w.DefinitionVersions).FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<PagedResult<WorkflowDefinition>> SearchAsync(string? q, WorkflowDefinitionStatus? status, string? category, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))        query = query.Where(w => w.Name.Contains(q));
        if (status.HasValue)                       query = query.Where(w => w.Status   == status);
        if (!string.IsNullOrWhiteSpace(category))  query = query.Where(w => w.Category == category);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(w => w.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<WorkflowDefinition>(items, page, pageSize, total);
    }
}

// ── IWorkflowInstanceRepository ──────────────────────────────────────────────

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowInstance?> GetWithTasksAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<WorkflowInstance>> SearchAsync(Guid? definitionId, WorkflowInstanceStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(WorkflowInstance instance, CancellationToken ct = default);
    void Update(WorkflowInstance instance);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class WorkflowInstanceRepository(EaiosDbContext db) : RepositoryBase<WorkflowInstance>(db), IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> GetWithTasksAsync(Guid id, CancellationToken ct = default) =>
        await Set.Include(i => i.Tasks).FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<PagedResult<WorkflowInstance>> SearchAsync(Guid? definitionId, WorkflowInstanceStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().AsQueryable();
        if (definitionId.HasValue) q = q.Where(i => i.DefinitionId == definitionId);
        if (status.HasValue)       q = q.Where(i => i.Status       == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(i => i.StartedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<WorkflowInstance>(items, page, pageSize, total);
    }
}

// ── IWorkflowTaskRepository ──────────────────────────────────────────────────

public interface IWorkflowTaskRepository
{
    Task<WorkflowTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTask>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default);
    Task<PagedResult<WorkflowTask>> GetAssignedToUserAsync(Guid userId, WorkflowTaskStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTask>> GetOverdueAsync(CancellationToken ct = default);
    Task AddAsync(WorkflowTask task, CancellationToken ct = default);
    void Update(WorkflowTask task);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class WorkflowTaskRepository(EaiosDbContext db) : RepositoryBase<WorkflowTask>(db), IWorkflowTaskRepository
{
    public async Task<IReadOnlyList<WorkflowTask>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default) =>
        await Set.Where(t => t.InstanceId == instanceId).OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public async Task<PagedResult<WorkflowTask>> GetAssignedToUserAsync(Guid userId, WorkflowTaskStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.Where(t => t.AssigneeId == userId);
        if (status.HasValue) q = q.Where(t => t.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(t => t.DueAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<WorkflowTask>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<WorkflowTask>> GetOverdueAsync(CancellationToken ct = default) =>
        await Set.Where(t => t.Status == WorkflowTaskStatus.Pending && t.DueAt < DateTime.UtcNow)
                 .OrderBy(t => t.DueAt)
                 .ToListAsync(ct);
}
