using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Domain.Analytics;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

// ── ISavedSearchRepository ───────────────────────────────────────────────────

public interface ISavedSearchRepository
{
    Task<SavedSearch?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SavedSearch>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(SavedSearch search, CancellationToken ct = default);
    void Update(SavedSearch search);
    void SoftDelete(SavedSearch search);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class SavedSearchRepository(EaiosDbContext db) : RepositoryBase<SavedSearch>(db), ISavedSearchRepository
{
    public async Task<IReadOnlyList<SavedSearch>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
        await Set.Where(s => s.UserId == userId || s.IsShared)
                 .OrderByDescending(s => s.LastExecutedAt)
                 .ToListAsync(ct);
}

// ── INotificationRepository ──────────────────────────────────────────────────

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Notification>> GetByRecipientAsync(Guid recipientId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid recipientId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid recipientId, CancellationToken ct = default);
    Task AddAsync(Notification notification, CancellationToken ct = default);
    void Update(Notification notification);
    void SoftDelete(Notification notification);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class NotificationRepository(EaiosDbContext db) : RepositoryBase<Notification>(db), INotificationRepository
{
    public async Task<PagedResult<Notification>> GetByRecipientAsync(Guid recipientId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.Where(n => n.RecipientId == recipientId);
        if (unreadOnly == true) q = q.Where(n => n.ReadAt == null);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Notification>(items, page, pageSize, total);
    }

    public async Task<int> GetUnreadCountAsync(Guid recipientId, CancellationToken ct = default) =>
        await Set.CountAsync(n => n.RecipientId == recipientId && n.ReadAt == null, ct);

    public async Task MarkAllReadAsync(Guid recipientId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var unread = await Set.Where(n => n.RecipientId == recipientId && n.ReadAt == null).ToListAsync(ct);
        foreach (var n in unread) n.MarkRead();
    }
}

// ── IAnalyticsEventRepository ────────────────────────────────────────────────

public interface IAnalyticsEventRepository
{
    Task AddAsync(AnalyticsEvent evt, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<AnalyticsEvent> events, CancellationToken ct = default);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class AnalyticsEventRepository(EaiosDbContext db) : RepositoryBase<AnalyticsEvent>(db), IAnalyticsEventRepository
{
    public override async Task AddRangeAsync(IEnumerable<AnalyticsEvent> events, CancellationToken ct = default) =>
        await db.AnalyticsEvents.AddRangeAsync(events, ct);
}

// ── IConnectorInstanceRepository ─────────────────────────────────────────────

public interface IConnectorInstanceRepository
{
    Task<ConnectorInstance?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ConnectorInstance>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(ConnectorInstance instance, CancellationToken ct = default);
    void Update(ConnectorInstance instance);
    void SoftDelete(ConnectorInstance instance);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class ConnectorInstanceRepository(EaiosDbContext db) : RepositoryBase<ConnectorInstance>(db), IConnectorInstanceRepository
{
    public async Task<IReadOnlyList<ConnectorInstance>> GetAllAsync(CancellationToken ct = default) =>
        await Set.Include(c => c.SyncJobs).OrderBy(c => c.Name).ToListAsync(ct);
}

// ── ISyncJobRepository ───────────────────────────────────────────────────────

public interface ISyncJobRepository
{
    Task<SyncJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SyncJob>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default);
    Task<IReadOnlyList<SyncJob>> GetDueAsync(CancellationToken ct = default);
    Task AddAsync(SyncJob job, CancellationToken ct = default);
    void Update(SyncJob job);
    void SoftDelete(SyncJob job);
    Task<int> SaveAsync(CancellationToken ct = default);
}

public sealed class SyncJobRepository(EaiosDbContext db) : RepositoryBase<SyncJob>(db), ISyncJobRepository
{
    public async Task<IReadOnlyList<SyncJob>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default) =>
        await Set.Where(j => j.ConnectorInstanceId == instanceId).ToListAsync(ct);

    public async Task<IReadOnlyList<SyncJob>> GetDueAsync(CancellationToken ct = default) =>
        await Set.Where(j => j.Status == SyncJobStatus.Active && j.NextRunAt <= DateTime.UtcNow).ToListAsync(ct);
}
