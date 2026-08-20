using EAIOS.Api.Application.Analytics;
using EAIOS.Api.Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Rapports asynchrones et exports.
/// La demande crée un job persistant, traité hors requête par <see cref="ReportGenerationWorker"/> ;
/// le client scrute ensuite le statut puis télécharge le fichier généré.
/// Route : /api/v1/analytics/reports
/// </summary>
[Route("api/v1/analytics/reports")]
[Authorize]
public sealed class ReportsController(
    IReportService reports,
    ReportQueueSignal queueSignal) : V1ApiController
{
    // ── POST /api/v1/analytics/reports ────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> GenerateReport([FromBody] GenerateReportRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var job = await reports.RequestAsync(TenantId, req, ActorId.Value, ct);

            // Réveille immédiatement le worker plutôt que d'attendre le prochain scrutin.
            queueSignal.Notify();

            return Accepted(
                $"/api/v1/analytics/reports/{job.Id}/status",
                Application.Common.Models.ApiResponse.Wrap(ReportService.Map(job)));
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    // ── GET /api/v1/analytics/reports ─────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ListReports(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var result = await reports.ListAsync(ActorId.Value, page, pageSize, ct);
        return OkList(result.Items.Select(ReportService.Map).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    // ── GET /api/v1/analytics/reports/{id}/status ─────────────────────────────
    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetReportStatus(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var job = await reports.GetAsync(id, ActorId.Value, ct);
            return Ok200(ReportService.Map(job));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Rapport introuvable.");
        }
    }

    // ── GET /api/v1/analytics/reports/{id}/download ───────────────────────────
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadReport(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var download = await reports.DownloadAsync(id, ActorId.Value, ct);
            return File(download.Content, download.ContentType, download.FileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Rapport introuvable.");
        }
        catch (InvalidOperationException ex)
        {
            // Encore en cours, échoué, ou expiré : l'état est décrit dans le message.
            return Conflict(ex.Message);
        }
    }
}
