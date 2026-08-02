using EAIOS.Api.Application.Analytics;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

namespace EAIOS.Api.Application.Analytics;

public sealed class AnalyticsQueryService(
    IAnalyticsEventRepository analyticsRepo) : IAnalyticsQueryService
{
    public async Task<DashboardDto> GetDashboardAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        // En production : requêtes SQL agrégées depuis analyticsRepo / vues matérialisées
        // Stub pour l'instant
        return await Task.FromResult(new DashboardDto(
            ActiveUsers: 0,
            NewUsersThisMonth: 0,
            DocumentsUploaded: 0,
            StorageUsedBytes: 0,
            StorageQuotaBytes: 0,
            SearchesExecuted: 0,
            AgentExecutions: 0,
            TotalAiCostUsd: 0.0m,
            WorkflowsCompleted: 0,
            WorkflowsInProgress: 0,
            UsageByDate: [],
            UsageByDepartment: []
        ));
    }

    public async Task<SearchAnalyticsDto> GetSearchAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        return await Task.FromResult(new SearchAnalyticsDto(
            TopQueries: [],
            ZeroResultQueries: [],
            AvgClickThroughRate: 0f,
            TotalSearches: 0,
            AvgResultsPerQuery: 0f
        ));
    }

    public async Task<AgentAnalyticsDto> GetAgentAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        return await Task.FromResult(new AgentAnalyticsDto(
            TotalExecutions: 0,
            SuccessfulExecutions: 0,
            FailedExecutions: 0,
            TotalCostUsd: 0.0m,
            AvgDurationMs: 0f,
            TotalTokens: 0,
            ByAgent: []
        ));
    }

    public async Task<WorkflowAnalyticsDto> GetWorkflowAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        return await Task.FromResult(new WorkflowAnalyticsDto(
            TotalInstances: 0,
            CompletedInstances: 0,
            FailedInstances: 0,
            ActiveInstances: 0,
            AvgCompletionTimeHours: 0f,
            SlaBreaches: 0,
            ByDefinition: []
        ));
    }

    public async Task<object> SummaryAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var summary = new
        {
            Period = period,
            Documents = new { Total = 0, Uploaded = 0, Indexed = 0, StorageGb = 0.0 },
            Agents = new { Total = 0, Executions = 0, SuccessRate = 0.0, AvgDurationMs = 0 },
            Workflows = new { Total = 0, Running = 0, Completed = 0, Failed = 0 },
            Knowledge = new { Items = 0, AskQueries = 0, AvgRating = 0.0 },
            Users = new { Active = 0, Invited = 0, MfaEnabled = 0 },
            Note = "Données agrégées temps-réel disponibles en production via PostgreSQL + Redis."
        };
        return await Task.FromResult(summary);
    }

    public async Task<object> UsageAsync(Guid tenantId, string metric, string period, string granularity, CancellationToken ct = default)
    {
        return await Task.FromResult(new
        {
            Metric = metric,
            Period = period,
            Granularity = granularity,
            Series = Array.Empty<object>(),
            Note = "Séries temporelles disponibles en production."
        });
    }

    public async Task<object> TopResourcesAsync(Guid tenantId, string type, int limit, string period, CancellationToken ct = default)
    {
        return await Task.FromResult(new
        {
            Type = type,
            Period = period,
            Items = Array.Empty<object>()
        });
    }
}
