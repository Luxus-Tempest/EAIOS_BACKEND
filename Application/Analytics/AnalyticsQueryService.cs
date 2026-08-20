using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EAIOS.Api.Application.Analytics;

/// <summary>
/// Agrégations analytiques calculées directement sur les tables métier
/// (documents, exécutions d'agents, instances de workflow, utilisateurs) et sur
/// le journal <c>analytics.events</c> alimenté par <see cref="IAnalyticsTracker"/>.
///
/// Les Global Query Filters du DbContext garantissent que chaque agrégat
/// reste borné au tenant courant.
/// </summary>
public sealed class AnalyticsQueryService(
    EaiosDbContext db,
    PlatformDbContext platformDb) : IAnalyticsQueryService
{
    /// <summary>Nombre maximum d'évènements bruts chargés pour les agrégats faits en mémoire.</summary>
    private const int EventScanLimit = 50_000;

    // ═════════════════════════════════════════════════════════════════════════
    // DASHBOARD
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<DashboardDto> GetDashboardAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var activeUsers = await db.Users
            .CountAsync(u => u.Status == UserStatus.Active && u.LastLoginAt >= from, ct);

        var newUsersThisMonth = await db.Users
            .CountAsync(u => u.CreatedAt >= monthStart, ct);

        var documentsUploaded = await db.Documents
            .CountAsync(d => d.CreatedAt >= from && d.CreatedAt < to, ct);

        var storageUsed = await db.Documents.SumAsync(d => (long?)d.FileSizeBytes, ct) ?? 0L;

        var org = await platformDb.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == tenantId, ct);
        var storageQuota = org?.StorageQuotaBytes ?? 0L;

        // Compter les deux types : une recherche sans resultat reste une recherche.
        var searchesExecuted = await db.AnalyticsEvents
            .CountAsync(e => (e.EventType == AnalyticsEventTypes.SearchExecuted
                           || e.EventType == AnalyticsEventTypes.SearchZeroResults)
                          && e.OccurredAt >= from && e.OccurredAt < to, ct);

        var agentExecutions = await db.AgentExecutions
            .CountAsync(e => e.StartedAt >= from && e.StartedAt < to, ct);

        var aiCost = await db.AgentExecutions
            .Where(e => e.StartedAt >= from && e.StartedAt < to)
            .SumAsync(e => (decimal?)e.CostUsd, ct) ?? 0m;

        var workflowsCompleted = await db.WorkflowInstances
            .CountAsync(w => w.Status == WorkflowInstanceStatus.Completed
                          && w.CompletedAt >= from && w.CompletedAt < to, ct);

        var workflowsInProgress = await db.WorkflowInstances
            .CountAsync(w => w.Status == WorkflowInstanceStatus.Executing
                          || w.Status == WorkflowInstanceStatus.WaitingForApproval
                          || w.Status == WorkflowInstanceStatus.Paused, ct);

        var usageByDate       = await BuildUsageByDateAsync(from, to, ct);
        var usageByDepartment = await BuildUsageByDepartmentAsync(from, to, ct);

        return new DashboardDto(
            ActiveUsers:         activeUsers,
            NewUsersThisMonth:   newUsersThisMonth,
            DocumentsUploaded:   documentsUploaded,
            StorageUsedBytes:    storageUsed,
            StorageQuotaBytes:   storageQuota,
            SearchesExecuted:    searchesExecuted,
            AgentExecutions:     agentExecutions,
            TotalAiCostUsd:      aiCost,
            WorkflowsCompleted:  workflowsCompleted,
            WorkflowsInProgress: workflowsInProgress,
            UsageByDate:         usageByDate,
            UsageByDepartment:   usageByDepartment);
    }

    private async Task<IReadOnlyList<UsageByDateDto>> BuildUsageByDateAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        // Un point par jour de la fenêtre — les jours sans activité restent présents à zéro
        // pour que les graphes du front n'aient pas de trous.
        var days = Math.Max(1, (int)Math.Ceiling((to - from).TotalDays));
        if (days > 366) days = 366;

        var docsByDay = await db.Documents
            .Where(d => d.CreatedAt >= from && d.CreatedAt < to)
            .GroupBy(d => d.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var agentsByDay = await db.AgentExecutions
            .Where(e => e.StartedAt >= from && e.StartedAt < to)
            .GroupBy(e => e.StartedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var events = await db.AnalyticsEvents
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to)
            .Select(e => new { e.OccurredAt, e.EventType, e.ActorId })
            .Take(EventScanLimit)
            .ToListAsync(ct);

        var searchesByDay = events
            .Where(e => e.EventType is AnalyticsEventTypes.SearchExecuted
                                    or AnalyticsEventTypes.SearchZeroResults)
            .GroupBy(e => e.OccurredAt.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var activeByDay = events
            .Where(e => e.ActorId.HasValue)
            .GroupBy(e => e.OccurredAt.Date)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ActorId!.Value).Distinct().Count());

        var docs   = docsByDay.ToDictionary(x => x.Day, x => x.Count);
        var agents = agentsByDay.ToDictionary(x => x.Day, x => x.Count);

        var series = new List<UsageByDateDto>(days);
        for (var i = 0; i < days; i++)
        {
            var day = from.Date.AddDays(i);
            series.Add(new UsageByDateDto(
                Date:            day,
                ActiveUsers:     activeByDay.GetValueOrDefault(day),
                Documents:       docs.GetValueOrDefault(day),
                Searches:        searchesByDay.GetValueOrDefault(day),
                AgentExecutions: agents.GetValueOrDefault(day)));
        }
        return series;
    }

    private async Task<IReadOnlyList<UsageByDepartmentDto>> BuildUsageByDepartmentAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var departments = await db.Departments
            .Select(d => new { d.Id, d.Name, d.MemberCount })
            .ToListAsync(ct);

        if (departments.Count == 0) return [];

        var docsByDept = await db.Documents
            .Where(d => d.CreatedAt >= from && d.CreatedAt < to && d.DepartmentId != null)
            .GroupBy(d => d.DepartmentId!.Value)
            .Select(g => new { DeptId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var docs = docsByDept.ToDictionary(x => x.DeptId, x => x.Count);

        // Les évènements de recherche portent l'acteur, pas le département :
        // on rattache via l'appartenance de l'utilisateur.
        var userDept = await db.Users
            .Where(u => u.Department != null)
            .Select(u => new { u.Id, u.Department })
            .ToListAsync(ct);
        var deptByUser = userDept
            .Where(u => !string.IsNullOrWhiteSpace(u.Department))
            .ToDictionary(u => u.Id, u => u.Department!);

        var searchEvents = await db.AnalyticsEvents
            .Where(e => e.EventType == AnalyticsEventTypes.SearchExecuted
                     && e.OccurredAt >= from && e.OccurredAt < to && e.ActorId != null)
            .Select(e => e.ActorId!.Value)
            .Take(EventScanLimit)
            .ToListAsync(ct);

        var searchesByDeptName = searchEvents
            .Select(actorId => deptByUser.GetValueOrDefault(actorId))
            .Where(name => name is not null)
            .GroupBy(name => name!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return departments
            .Select(d => new UsageByDepartmentDto(
                Department: d.Name,
                Users:      d.MemberCount,
                Documents:  docs.GetValueOrDefault(d.Id),
                Searches:   searchesByDeptName.GetValueOrDefault(d.Name)))
            .OrderByDescending(d => d.Documents)
            .ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SEARCH ANALYTICS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<SearchAnalyticsDto> GetSearchAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);

        // Le texte de la requête vit dans PropertiesJson : on agrège en mémoire
        // plutôt que d'imposer des opérateurs JSON spécifiques au provider SQL.
        var rows = await db.AnalyticsEvents
            .Where(e => (e.EventType == AnalyticsEventTypes.SearchExecuted
                      || e.EventType == AnalyticsEventTypes.SearchZeroResults)
                     && e.OccurredAt >= from && e.OccurredAt < to)
            .Select(e => new { e.EventType, e.PropertiesJson })
            .Take(EventScanLimit)
            .ToListAsync(ct);

        var parsed = rows
            .Select(r => new
            {
                r.EventType,
                Props = ParseSearchProperties(r.PropertiesJson)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Props.Query))
            .ToList();

        // « executed » = recherches ayant renvoye au moins un resultat (alimente le top).
        // « allSearches » = toutes les recherches, y compris infructueuses (alimente les totaux).
        var executed    = parsed.Where(p => p.EventType == AnalyticsEventTypes.SearchExecuted).ToList();
        var allSearches = parsed;

        var topQueries = executed
            .GroupBy(p => p.Props.Query!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PopularQueryDto(
                Query:    g.Key,
                Count:    g.Count(),
                AvgScore: g.Average(x => x.Props.TopScore ?? 0f)))
            .OrderByDescending(q => q.Count)
            .Take(20)
            .ToList();

        var zeroResultQueries = parsed
            .Where(p => p.EventType == AnalyticsEventTypes.SearchZeroResults || p.Props.ResultCount == 0)
            .GroupBy(p => p.Props.Query!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PopularQueryDto(g.Key, g.Count(), 0f))
            .OrderByDescending(q => q.Count)
            .Take(20)
            .ToList();

        var totalSearches      = allSearches.Count;
        var avgResultsPerQuery = totalSearches == 0 ? 0f : (float)allSearches.Average(e => e.Props.ResultCount ?? 0);
        var clicked            = allSearches.Count(e => e.Props.Clicked == true);
        var avgCtr             = totalSearches == 0 ? 0f : (float)clicked / totalSearches;

        return new SearchAnalyticsDto(
            TopQueries:          topQueries,
            ZeroResultQueries:   zeroResultQueries,
            AvgClickThroughRate: avgCtr,
            TotalSearches:       totalSearches,
            AvgResultsPerQuery:  avgResultsPerQuery);
    }

    private sealed record SearchProps(string? Query, int? ResultCount, float? TopScore, bool? Clicked);

    private static SearchProps ParseSearchProperties(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            return new SearchProps(
                Query:       TryGetString(root, "query"),
                ResultCount: TryGetInt(root, "resultCount"),
                TopScore:    TryGetFloat(root, "topScore"),
                Clicked:     TryGetBool(root, "clicked"));
        }
        catch (JsonException)
        {
            return new SearchProps(null, null, null, null);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // AGENT ANALYTICS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<AgentAnalyticsDto> GetAgentAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);

        var executions = await db.AgentExecutions
            .Where(e => e.StartedAt >= from && e.StartedAt < to)
            .Select(e => new
            {
                e.AgentId,
                e.Status,
                e.CostUsd,
                e.TotalTokens,
                e.Duration
            })
            .ToListAsync(ct);

        if (executions.Count == 0)
            return new AgentAnalyticsDto(0, 0, 0, 0m, 0f, 0L, []);

        var successful = executions.Count(e => e.Status == AgentExecutionStatus.Completed);
        var failed     = executions.Count(e => e.Status == AgentExecutionStatus.Failed
                                            || e.Status == AgentExecutionStatus.TimedOut);

        var withDuration = executions.Where(e => e.Duration.HasValue).ToList();
        var avgDuration  = withDuration.Count == 0
            ? 0f
            : (float)withDuration.Average(e => e.Duration!.Value.TotalMilliseconds);

        var agentNames = await db.Agents
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        var nameById = agentNames.ToDictionary(a => a.Id, a => a.Name);

        var byAgent = executions
            .GroupBy(e => e.AgentId)
            .Select(g =>
            {
                var count = g.Count();
                var ok    = g.Count(x => x.Status == AgentExecutionStatus.Completed);
                return new AgentUsageDto(
                    AgentId:     g.Key,
                    AgentName:   nameById.GetValueOrDefault(g.Key) ?? "(agent supprimé)",
                    Executions:  count,
                    CostUsd:     g.Sum(x => x.CostUsd),
                    SuccessRate: count == 0 ? 0f : (float)ok / count);
            })
            .OrderByDescending(a => a.Executions)
            .ToList();

        return new AgentAnalyticsDto(
            TotalExecutions:      executions.Count,
            SuccessfulExecutions: successful,
            FailedExecutions:     failed,
            TotalCostUsd:         executions.Sum(e => e.CostUsd),
            AvgDurationMs:        avgDuration,
            TotalTokens:          executions.Sum(e => (long)e.TotalTokens),
            ByAgent:              byAgent);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // WORKFLOW ANALYTICS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<WorkflowAnalyticsDto> GetWorkflowAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);

        var instances = await db.WorkflowInstances
            .Where(w => w.StartedAt >= from && w.StartedAt < to)
            .Select(w => new
            {
                w.DefinitionId,
                w.Status,
                w.StartedAt,
                w.CompletedAt,
                w.DueAt
            })
            .ToListAsync(ct);

        if (instances.Count == 0)
            return new WorkflowAnalyticsDto(0, 0, 0, 0, 0f, 0, []);

        var completed = instances.Count(w => w.Status == WorkflowInstanceStatus.Completed);
        var failed    = instances.Count(w => w.Status == WorkflowInstanceStatus.Failed
                                          || w.Status == WorkflowInstanceStatus.TimedOut);
        var active    = instances.Count(w => w.Status == WorkflowInstanceStatus.Executing
                                          || w.Status == WorkflowInstanceStatus.WaitingForApproval
                                          || w.Status == WorkflowInstanceStatus.Paused
                                          || w.Status == WorkflowInstanceStatus.Initialized);

        var finished = instances.Where(w => w.CompletedAt.HasValue).ToList();
        var avgHours = finished.Count == 0
            ? 0f
            : (float)finished.Average(w => (w.CompletedAt!.Value - w.StartedAt).TotalHours);

        // Une échéance dépassée compte comme rupture de SLA, qu'elle soit close en retard
        // ou toujours ouverte au-delà de la date due.
        var slaBreaches = instances.Count(w => w.DueAt.HasValue
            && ((w.CompletedAt.HasValue && w.CompletedAt > w.DueAt)
             || (!w.CompletedAt.HasValue && w.DueAt < DateTime.UtcNow)));

        var defNames = await db.WorkflowDefinitions
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(ct);
        var nameById = defNames.ToDictionary(d => d.Id, d => d.Name);

        var byDefinition = instances
            .GroupBy(w => w.DefinitionId)
            .Select(g =>
            {
                var count = g.Count();
                var ok    = g.Count(x => x.Status == WorkflowInstanceStatus.Completed);
                var done  = g.Where(x => x.CompletedAt.HasValue).ToList();
                return new WorkflowUsageDto(
                    DefinitionId:     g.Key,
                    Name:             nameById.GetValueOrDefault(g.Key) ?? "(workflow supprimé)",
                    Executions:       count,
                    CompletionRate:   count == 0 ? 0f : (float)ok / count,
                    AvgDurationHours: done.Count == 0
                        ? 0f
                        : (float)done.Average(x => (x.CompletedAt!.Value - x.StartedAt).TotalHours));
            })
            .OrderByDescending(d => d.Executions)
            .ToList();

        return new WorkflowAnalyticsDto(
            TotalInstances:         instances.Count,
            CompletedInstances:     completed,
            FailedInstances:        failed,
            ActiveInstances:        active,
            AvgCompletionTimeHours: avgHours,
            SlaBreaches:            slaBreaches,
            ByDefinition:           byDefinition);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // VUES DE COMPATIBILITÉ
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<object> SummaryAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);

        var totalDocuments = await db.Documents.CountAsync(ct);
        var uploaded       = await db.Documents.CountAsync(d => d.CreatedAt >= from && d.CreatedAt < to, ct);
        var indexed        = await db.Documents.CountAsync(d => d.IndexingStatus == IndexingStatus.Indexed, ct);
        var storageBytes   = await db.Documents.SumAsync(d => (long?)d.FileSizeBytes, ct) ?? 0L;

        var agentStats = await GetAgentAnalyticsAsync(tenantId, period, ct);
        var totalAgents = await db.Agents.CountAsync(ct);

        var wfStats     = await GetWorkflowAnalyticsAsync(tenantId, period, ct);
        var totalWfDefs = await db.WorkflowDefinitions.CountAsync(ct);

        var knowledgeItems = await db.KnowledgeItems.CountAsync(ct);
        var askQueries     = await db.AnalyticsEvents.CountAsync(
            e => e.EventType == AnalyticsEventTypes.KnowledgeAsked
              && e.OccurredAt >= from && e.OccurredAt < to, ct);

        var activeUsers  = await db.Users.CountAsync(u => u.Status == UserStatus.Active, ct);
        var invitedUsers = await db.Users.CountAsync(u => u.Status == UserStatus.PendingVerification, ct);
        var mfaUsers     = await db.Users.CountAsync(u => u.IsMfaEnabled, ct);

        return new
        {
            Period = period,
            From   = from,
            To     = to,
            Documents = new
            {
                Total     = totalDocuments,
                Uploaded  = uploaded,
                Indexed   = indexed,
                StorageGb = Math.Round(storageBytes / 1024d / 1024d / 1024d, 3)
            },
            Agents = new
            {
                Total         = totalAgents,
                Executions    = agentStats.TotalExecutions,
                SuccessRate   = agentStats.TotalExecutions == 0
                    ? 0d
                    : Math.Round((double)agentStats.SuccessfulExecutions / agentStats.TotalExecutions, 4),
                AvgDurationMs = Math.Round(agentStats.AvgDurationMs, 1)
            },
            Workflows = new
            {
                Total     = totalWfDefs,
                Running   = wfStats.ActiveInstances,
                Completed = wfStats.CompletedInstances,
                Failed    = wfStats.FailedInstances
            },
            Knowledge = new
            {
                Items      = knowledgeItems,
                AskQueries = askQueries
            },
            Users = new
            {
                Active     = activeUsers,
                Invited    = invitedUsers,
                MfaEnabled = mfaUsers
            }
        };
    }

    public async Task<object> UsageAsync(Guid tenantId, string metric, string period, string granularity, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);
        var bucket     = ResolveGranularity(granularity);

        // Chaque métrique se ramène à une liste d'horodatages, ensuite regroupée par bucket.
        List<DateTime> timestamps = metric.ToLowerInvariant() switch
        {
            "documents" or "document.uploaded" => await db.Documents
                .Where(d => d.CreatedAt >= from && d.CreatedAt < to)
                .Select(d => d.CreatedAt).ToListAsync(ct),

            "agents" or "agent.executions" => await db.AgentExecutions
                .Where(e => e.StartedAt >= from && e.StartedAt < to)
                .Select(e => e.StartedAt).ToListAsync(ct),

            "workflows" or "workflow.instances" => await db.WorkflowInstances
                .Where(w => w.StartedAt >= from && w.StartedAt < to)
                .Select(w => w.StartedAt).ToListAsync(ct),

            "users" or "user.signups" => await db.Users
                .Where(u => u.CreatedAt >= from && u.CreatedAt < to)
                .Select(u => u.CreatedAt).ToListAsync(ct),

            "searches" or "search.executed" => await db.AnalyticsEvents
                .Where(e => (e.EventType == AnalyticsEventTypes.SearchExecuted
                          || e.EventType == AnalyticsEventTypes.SearchZeroResults)
                         && e.OccurredAt >= from && e.OccurredAt < to)
                .Select(e => e.OccurredAt).Take(EventScanLimit).ToListAsync(ct),

            // Métrique libre : on la cherche telle quelle dans le journal d'évènements.
            _ => await db.AnalyticsEvents
                .Where(e => e.EventType == metric && e.OccurredAt >= from && e.OccurredAt < to)
                .Select(e => e.OccurredAt).Take(EventScanLimit).ToListAsync(ct)
        };

        var grouped = timestamps
            .GroupBy(t => Truncate(t, bucket))
            .ToDictionary(g => g.Key, g => g.Count());

        var series = new List<object>();
        for (var cursor = Truncate(from, bucket); cursor < to; cursor = Advance(cursor, bucket))
            series.Add(new { Timestamp = cursor, Value = grouped.GetValueOrDefault(cursor) });

        return new
        {
            Metric      = metric,
            Period      = period,
            Granularity = bucket.ToString().ToLowerInvariant(),
            From        = from,
            To          = to,
            Total       = timestamps.Count,
            Series      = series
        };
    }

    public async Task<object> TopResourcesAsync(Guid tenantId, string type, int limit, string period, CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(period);
        if (limit <= 0) limit = 10;
        if (limit > 100) limit = 100;

        object items = type.ToLowerInvariant() switch
        {
            "documents" or "document" => await db.Documents
                .OrderByDescending(d => d.ViewCount).ThenByDescending(d => d.DownloadCount)
                .Take(limit)
                .Select(d => new
                {
                    d.Id, d.Title, d.ResourceType, d.ViewCount, d.DownloadCount, d.FileSizeBytes, d.CreatedAt
                })
                .ToListAsync(ct),

            "agents" or "agent" => (await db.AgentExecutions
                .Where(e => e.StartedAt >= from && e.StartedAt < to)
                .GroupBy(e => e.AgentId)
                .Select(g => new { AgentId = g.Key, Executions = g.Count(), Cost = g.Sum(x => x.CostUsd) })
                .OrderByDescending(x => x.Executions)
                .Take(limit)
                .ToListAsync(ct))
                .Join(await db.Agents.Select(a => new { a.Id, a.Name }).ToListAsync(ct),
                      x => x.AgentId, a => a.Id,
                      (x, a) => new { x.AgentId, a.Name, x.Executions, x.Cost })
                .Cast<object>().ToList(),

            "knowledge" => await db.KnowledgeItems
                .OrderByDescending(k => k.ViewCount)
                .Take(limit)
                .Select(k => new { k.Id, k.Title, k.Type, k.Status, k.ViewCount, k.CreatedAt })
                .ToListAsync(ct),

            "searches" or "queries" => (await db.AnalyticsEvents
                .Where(e => e.EventType == AnalyticsEventTypes.SearchExecuted
                         && e.OccurredAt >= from && e.OccurredAt < to)
                .Select(e => e.PropertiesJson)
                .Take(EventScanLimit)
                .ToListAsync(ct))
                .Select(ParseSearchProperties)
                .Where(p => !string.IsNullOrWhiteSpace(p.Query))
                .GroupBy(p => p.Query!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Query = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(limit)
                .Cast<object>().ToList(),

            _ => Array.Empty<object>()
        };

        return new { Type = type, Period = period, From = from, To = to, Limit = limit, Items = items };
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS — période & granularité
    // ═════════════════════════════════════════════════════════════════════════

    private enum Bucket { Hour, Day, Week, Month }

    /// <summary>
    /// Interprète les périodes usuelles : <c>24h</c>, <c>7d</c>, <c>30d</c>, <c>12w</c>,
    /// <c>6m</c>, <c>1y</c>, <c>today</c>, <c>mtd</c>, <c>ytd</c>. Défaut : 30 jours.
    /// </summary>
    internal static (DateTime From, DateTime To) ResolvePeriod(string? period)
    {
        var now = DateTime.UtcNow;
        var to  = now.AddSeconds(1);   // borne haute exclusive : inclut l'instant présent
        var p   = (period ?? "30d").Trim().ToLowerInvariant();

        switch (p)
        {
            case "today": return (now.Date, to);
            case "mtd":   return (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), to);
            case "ytd":   return (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), to);
            case "all":   return (DateTime.UnixEpoch, to);
        }

        var unit = p.Length > 0 ? p[^1] : 'd';
        if (int.TryParse(p[..^1], out var n) && n > 0)
        {
            return unit switch
            {
                'h' => (now.AddHours(-n),  to),
                'd' => (now.Date.AddDays(-(n - 1)), to),
                'w' => (now.Date.AddDays(-7 * n),   to),
                'm' => (now.AddMonths(-n), to),
                'y' => (now.AddYears(-n),  to),
                _   => (now.Date.AddDays(-29), to)
            };
        }

        return (now.Date.AddDays(-29), to);
    }

    private static Bucket ResolveGranularity(string? granularity) =>
        (granularity ?? "day").Trim().ToLowerInvariant() switch
        {
            "hour"  or "hourly"  => Bucket.Hour,
            "week"  or "weekly"  => Bucket.Week,
            "month" or "monthly" => Bucket.Month,
            _                    => Bucket.Day
        };

    private static DateTime Truncate(DateTime t, Bucket b) => b switch
    {
        Bucket.Hour  => new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc),
        Bucket.Week  => t.Date.AddDays(-(int)t.Date.DayOfWeek),
        Bucket.Month => new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        _            => t.Date
    };

    private static DateTime Advance(DateTime t, Bucket b) => b switch
    {
        Bucket.Hour  => t.AddHours(1),
        Bucket.Week  => t.AddDays(7),
        Bucket.Month => t.AddMonths(1),
        _            => t.AddDays(1)
    };

    // ── Lecture défensive du JSON de propriétés ───────────────────────────────
    private static string? TryGetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryGetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static float? TryGetFloat(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) ? f : null;

    private static bool? TryGetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;
}
