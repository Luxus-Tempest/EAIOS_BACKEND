using EAIOS.Api.Application.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Analytics : dashboard summary, métriques d'usage, top ressources.
/// Route : /api/v1/analytics
/// </summary>
[Route("api/v1/analytics")]
public sealed class AnalyticsController(
    IAnalyticsQueryService queryService) : V1ApiController
{
    // ── GET /api/v1/analytics/summary ─────────────────────────────────────────
    /// <summary>Résumé des métriques principales de l'organisation.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] string period = "30d",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var summary = await queryService.SummaryAsync(TenantId, period, ct);
        return Ok200(summary);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] string period = "30d",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var dashboard = await queryService.GetDashboardAsync(TenantId, period, ct);
        return Ok200(dashboard);
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchAnalytics(
        [FromQuery] string period = "30d",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var data = await queryService.GetSearchAnalyticsAsync(TenantId, period, ct);
        return Ok200(data);
    }

    [HttpGet("agents")]
    public async Task<IActionResult> AgentAnalytics(
        [FromQuery] string period = "30d",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var data = await queryService.GetAgentAnalyticsAsync(TenantId, period, ct);
        return Ok200(data);
    }

    [HttpGet("workflows")]
    public async Task<IActionResult> WorkflowAnalytics(
        [FromQuery] string period = "30d",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var data = await queryService.GetWorkflowAnalyticsAsync(TenantId, period, ct);
        return Ok200(data);
    }

    // ── GET /api/v1/analytics/usage ───────────────────────────────────────────
    /// <summary>Séries temporelles d'usage (pour graphiques).</summary>
    [HttpGet("usage")]
    public async Task<IActionResult> Usage(
        [FromQuery] string metric  = "requests",
        [FromQuery] string period  = "7d",
        [FromQuery] string granularity = "day",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var usage = await queryService.UsageAsync(TenantId, metric, period, granularity, ct);
        return Ok200(usage);
    }

    // ── GET /api/v1/analytics/top ─────────────────────────────────────────────
    /// <summary>Top N ressources les plus utilisées.</summary>
    [HttpGet("top")]
    public async Task<IActionResult> TopResources(
        [FromQuery] string type    = "documents",
        [FromQuery] int    limit   = 10,
        [FromQuery] string period  = "30d",
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var top = await queryService.TopResourcesAsync(TenantId, type, limit, period, ct);
        return Ok200(top);
    }
}
