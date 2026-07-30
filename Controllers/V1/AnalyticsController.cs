using EAIOS.Api.Application.Analytics;
using EAIOS.Api.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[Route("api/v1/analytics")]
public sealed class AnalyticsController : V1ApiController
{
    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        var dashboard = new DashboardDto(
            ActiveUsers: 42,
            NewUsersThisMonth: 8,
            DocumentsUploaded: 1250,
            StorageUsedBytes: 5_400_000_000,
            StorageQuotaBytes: 10_737_418_240,
            SearchesExecuted: 3400,
            AgentExecutions: 890,
            TotalAiCostUsd: 14.50m,
            WorkflowsCompleted: 120,
            WorkflowsInProgress: 15,
            UsageByDate: new List<UsageByDateDto>
            {
                new(DateTime.UtcNow.AddDays(-2), 38, 45, 120, 30),
                new(DateTime.UtcNow.AddDays(-1), 42, 60, 150, 45),
                new(DateTime.UtcNow, 40, 52, 140, 38)
            },
            UsageByDepartment: new List<UsageByDepartmentDto>
            {
                new("R&D", 15, 600, 1800),
                new("Legal", 8, 400, 900),
                new("HR", 10, 150, 500)
            });

        return Ok(ApiResponse.Wrap(dashboard));
    }
}
