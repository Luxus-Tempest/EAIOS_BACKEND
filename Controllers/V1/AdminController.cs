using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Application.Organization;
using EAIOS.Api.Application.Platform;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Seeds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Administration plateforme (super-admin uniquement).
/// Accès restreint au rôle platform.admin.
/// Route : /api/v1/admin
/// </summary>
[Route("api/v1/admin")]
[Authorize(Roles = "platform.admin,Admin")]
public sealed class AdminController(IPlatformAdminService adminService) : V1ApiController
{
    // ── GET /api/v1/admin/tenants ─────────────────────────────────────────────
    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants(
        [FromQuery] int    page     = 1,
        [FromQuery] int    pageSize = 20,
        [FromQuery] string? q       = null,
        CancellationToken  ct       = default)
    {
        var result = await adminService.ListTenantsAsync(page, pageSize, q, ct);
        return Ok(ApiResponse.List(result.Items, result.TotalCount, page, pageSize));
    }

    // ── GET /api/v1/admin/tenants/{id} ────────────────────────────────────────
    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await adminService.GetTenantAsync(id, ct);
            return Ok200(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var dto = await adminService.CreateTenantAsync(req, ActorId.Value, ct);
        return Created201("GetTenant", new { id = dto.Id }, dto);
    }

    // ── POST /api/v1/admin/tenants/{id}/suspend ───────────────────────────────
    [HttpPost("tenants/{id:guid}/suspend")]
    public async Task<IActionResult> SuspendTenant(Guid id, [FromBody] SuspendTenantRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var dto = await adminService.SuspendTenantAsync(id, req.Reason, ActorId.Value, ct);
            return Ok200(new { dto.Id, dto.Status, Reason = req.Reason });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── POST /api/v1/admin/tenants/{id}/reactivate ────────────────────────────
    [HttpPost("tenants/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTenant(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var dto = await adminService.ReactivateTenantAsync(id, ActorId.Value, ct);
            return Ok200(new { dto.Id, dto.Status });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("tenants/{id:guid}/stats")]
    public async Task<IActionResult> GetTenantStats(Guid id, CancellationToken ct)
    {
        try
        {
            var stats = await adminService.GetTenantStatsAsync(id, ct);
            return Ok200(stats);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPut("tenants/{id:guid}/license")]
    public async Task<IActionResult> UpdateTenantLicense(Guid id, [FromBody] UpdateLicenseRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var dto = await adminService.UpdateTenantLicenseAsync(id, req, ActorId.Value, ct);
            return Ok200(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── GET /api/v1/admin/audit-logs ─────────────────────────────────────────
    [HttpGet("audit-logs")]
    public async Task<IActionResult> ListAuditLogs(
        [FromQuery] Guid?   organizationId = null,
        [FromQuery] string? action         = null,
        [FromQuery] string? actorId        = null,
        [FromQuery] int     page           = 1,
        [FromQuery] int     pageSize       = 50,
        CancellationToken   ct             = default)
    {
        var result = await adminService.ListAuditLogsAsync(organizationId, action, actorId, page, pageSize, ct);
        return Ok(ApiResponse.List(result.Items, result.TotalCount, page, pageSize));
    }

    [HttpPost("audit-logs/export")]
    public async Task<IActionResult> ExportAuditLogs(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var jobId = await adminService.ExportAuditLogsAsync(ActorId.Value, ct);
        return Accepted(new { JobId = jobId, StatusUrl = $"/api/v1/admin/exports/{jobId}/status" });
    }

    // ── POST /api/v1/admin/seed ───────────────────────────────────────────────
    [HttpPost("seed")]
    public async Task<IActionResult> SeedPermissions(CancellationToken ct)
    {
        await SystemPermissionsSeed.SeedAsync(
            HttpContext.RequestServices.GetRequiredService<Infrastructure.Persistence.EaiosDbContext>(),
            TenantId,
            ct);
        return Ok200(new { message = "Permissions système ensemencées avec succès." });
    }

    // ── GET /api/v1/admin/feature-flags ──────────────────────────────────────
    [HttpGet("feature-flags")]
    public async Task<IActionResult> ListFeatureFlags(
        [FromQuery] Guid? organizationId,
        CancellationToken ct)
    {
        var flags = await adminService.ListFeatureFlagsAsync(organizationId, ct);
        return Ok200(flags);
    }

    // ── PUT /api/v1/admin/feature-flags/{id} ─────────────────────────────────
    [HttpPut("feature-flags/{id:guid}")]
    public async Task<IActionResult> UpdateFeatureFlag(Guid id, [FromBody] UpdateFeatureFlagRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var flag = await adminService.UpdateFeatureFlagAsync(id, req, ActorId.Value, ct);
            return Ok200(flag);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var status = await adminService.GetHealthStatusAsync(ct);
        return Ok200(status);
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var metrics = await adminService.GetMetricsAsync(ct);
        return Ok200(metrics);
    }
}

// ── Request models locaux ────────────────────────────────────────────────────
public sealed record SuspendTenantRequest(string Reason);
// UpdateFeatureFlagRequest défini dans Application.Platform.Dtos
