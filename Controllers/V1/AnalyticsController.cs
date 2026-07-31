using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Analytics : dashboard summary, métriques d'usage, top ressources.
/// Route : /api/v1/analytics
/// </summary>
[Route("api/v1/analytics")]
public sealed class AnalyticsController(
    IAnalyticsEventRepository analyticsRepo) : V1ApiController
{
    // ── GET /api/v1/analytics/summary ─────────────────────────────────────────
    /// <summary>Résumé des métriques principales de l'organisation.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] string period = "30d",
        CancellationToken ct = default)
    {
        // En production : requêtes SQL agrégées par période, avec cache Redis
        // Pour l'instant : stub avec structure complète pour le front-end
        var summary = new
        {
            Period = period,
            Documents = new
            {
                Total     = 0,
                Uploaded  = 0,
                Indexed   = 0,
                StorageGb = 0.0
            },
            Agents = new
            {
                Total      = 0,
                Executions = 0,
                SuccessRate = 0.0,
                AvgDurationMs = 0
            },
            Workflows = new
            {
                Total     = 0,
                Running   = 0,
                Completed = 0,
                Failed    = 0
            },
            Knowledge = new
            {
                Items      = 0,
                AskQueries = 0,
                AvgRating  = 0.0
            },
            Users = new
            {
                Active  = 0,
                Invited = 0,
                MfaEnabled = 0
            },
            Note = "Données agrégées temps-réel disponibles en production via PostgreSQL + Redis."
        };

        return Ok200(summary);
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
        // Stub — en production : time-series depuis PostgreSQL ou InfluxDB
        return Ok200(new
        {
            Metric      = metric,
            Period      = period,
            Granularity = granularity,
            Series      = Array.Empty<object>(),
            Note        = "Séries temporelles disponibles en production."
        });
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
        return Ok200(new
        {
            Type   = type,
            Period = period,
            Items  = Array.Empty<object>()
        });
    }
}
