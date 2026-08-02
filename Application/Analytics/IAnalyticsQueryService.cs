using EAIOS.Api.Application.Analytics;

namespace EAIOS.Api.Application.Analytics;

public interface IAnalyticsQueryService
{
    // Nouvelles méthodes canoniques
    Task<DashboardDto> GetDashboardAsync(Guid tenantId, string period, CancellationToken ct = default);
    Task<SearchAnalyticsDto> GetSearchAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default);
    Task<AgentAnalyticsDto> GetAgentAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default);
    Task<WorkflowAnalyticsDto> GetWorkflowAnalyticsAsync(Guid tenantId, string period, CancellationToken ct = default);
    
    // Vues de compatibilité (Legacy)
    Task<object> SummaryAsync(Guid tenantId, string period, CancellationToken ct = default);
    Task<object> UsageAsync(Guid tenantId, string metric, string period, string granularity, CancellationToken ct = default);
    Task<object> TopResourcesAsync(Guid tenantId, string type, int limit, string period, CancellationToken ct = default);
}
