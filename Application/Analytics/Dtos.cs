namespace EAIOS.Api.Application.Analytics;

public sealed record DashboardDto(
    int ActiveUsers,
    int NewUsersThisMonth,
    long DocumentsUploaded,
    long StorageUsedBytes,
    long StorageQuotaBytes,
    int SearchesExecuted,
    int AgentExecutions,
    decimal TotalAiCostUsd,
    int WorkflowsCompleted,
    int WorkflowsInProgress,
    IReadOnlyList<UsageByDateDto> UsageByDate,
    IReadOnlyList<UsageByDepartmentDto> UsageByDepartment);

public sealed record UsageByDateDto(DateTime Date, int ActiveUsers, int Documents, int Searches, int AgentExecutions);
public sealed record UsageByDepartmentDto(string Department, int Users, int Documents, int Searches);

public sealed record SearchAnalyticsDto(
    IReadOnlyList<PopularQueryDto> TopQueries,
    IReadOnlyList<PopularQueryDto> ZeroResultQueries,
    float AvgClickThroughRate,
    int TotalSearches,
    float AvgResultsPerQuery);

public sealed record PopularQueryDto(string Query, int Count, float AvgScore);

public sealed record AgentAnalyticsDto(
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    decimal TotalCostUsd,
    float AvgDurationMs,
    long TotalTokens,
    IReadOnlyList<AgentUsageDto> ByAgent);

public sealed record AgentUsageDto(Guid AgentId, string AgentName, int Executions, decimal CostUsd, float SuccessRate);

public sealed record WorkflowAnalyticsDto(
    int TotalInstances,
    int CompletedInstances,
    int FailedInstances,
    int ActiveInstances,
    float AvgCompletionTimeHours,
    int SlaBreaches,
    IReadOnlyList<WorkflowUsageDto> ByDefinition);

public sealed record WorkflowUsageDto(Guid DefinitionId, string Name, int Executions, float CompletionRate, float AvgDurationHours);

public sealed record GenerateReportRequest(
    string ReportType,
    DateTime DateFrom,
    DateTime DateTo,
    string Format = "pdf",
    Dictionary<string, object>? Parameters = null);

public sealed record ReportJobResult(string ReportId, string StatusUrl);
