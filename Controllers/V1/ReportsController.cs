using EAIOS.Api.Application.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Rapports asynchrones et exports.
/// Route : /api/v1/analytics/reports
/// </summary>
[Route("api/v1/analytics/reports")]
[Authorize]
public sealed class ReportsController : V1ApiController
{
    [HttpPost]
    public async Task<IActionResult> GenerateReport([FromBody] GenerateReportRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        
        // Stub : en production, publier un message dans une file d'attente
        var reportId = Guid.CreateVersion7().ToString("N");
        return Accepted(new ReportJobResult(reportId, $"/api/v1/analytics/reports/{reportId}/status"));
    }

    [HttpGet("{id}/status")]
    public async Task<IActionResult> GetReportStatus(string id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        
        // Stub
        return Ok200(new { Status = "Completed", DownloadUrl = $"/api/v1/analytics/reports/{id}/download" });
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadReport(string id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        
        // Stub : en production, vérifier l'URL signée et retourner le fichier
        return Ok200(new { Message = "Fichier rapport simulé." });
    }
}
