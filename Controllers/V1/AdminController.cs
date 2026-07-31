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
[Authorize(Roles = "platform.admin")]
public sealed class AdminController(PlatformDbContext platformDb) : V1ApiController
{
    // ── GET /api/v1/admin/tenants ─────────────────────────────────────────────
    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants(
        [FromQuery] int    page     = 1,
        [FromQuery] int    pageSize = 20,
        [FromQuery] string? q       = null,
        CancellationToken  ct       = default)
    {
        var query = platformDb.Organizations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.Name.Contains(q) || o.Slug.Contains(q));

        var total = await query.CountAsync(ct);
        var orgs  = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = orgs.Select(o => new TenantSummaryDto(
            o.Id, o.Name, o.Slug, o.Status.ToString(), o.PlanId,
            o.CurrentUsers, o.MaxUsers,
            o.StorageUsedBytes, o.StorageQuotaBytes,
            o.CreatedAt, o.TrialEndsAt)).ToList();

        return Ok(ApiResponse.List(dtos, total, page, pageSize));
    }

    // ── GET /api/v1/admin/tenants/{id} ────────────────────────────────────────
    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct);
        if (org == null) return NotFound();

        return Ok200(new TenantSummaryDto(
            org.Id, org.Name, org.Slug, org.Status.ToString(), org.PlanId,
            org.CurrentUsers, org.MaxUsers,
            org.StorageUsedBytes, org.StorageQuotaBytes,
            org.CreatedAt, org.TrialEndsAt));
    }

    // ── POST /api/v1/admin/tenants/{id}/suspend ───────────────────────────────
    [HttpPost("tenants/{id:guid}/suspend")]
    public async Task<IActionResult> SuspendTenant(Guid id, [FromBody] SuspendTenantRequest req, CancellationToken ct)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct);
        if (org == null) return NotFound();

        org.Status = Domain.Organization.OrganizationStatus.Suspended;
        await platformDb.SaveChangesAsync(ct);

        return Ok200(new { org.Id, org.Status, Reason = req.Reason });
    }

    // ── POST /api/v1/admin/tenants/{id}/reactivate ────────────────────────────
    [HttpPost("tenants/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTenant(Guid id, CancellationToken ct)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct);
        if (org == null) return NotFound();

        org.Status = Domain.Organization.OrganizationStatus.Active;
        await platformDb.SaveChangesAsync(ct);

        return Ok200(new { org.Id, org.Status });
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
        var query = platformDb.AuditEvents.AsQueryable();

        if (organizationId.HasValue)
            query = query.Where(e => e.OrganizationId == organizationId.Value);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(e => e.Action.Contains(action));
        if (!string.IsNullOrWhiteSpace(actorId))
            query = query.Where(e => e.ActorId.ToString() == actorId);

        var total  = await query.CountAsync(ct);
        var events = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = events.Select(e => new AuditEventDto(
            e.Id, e.OrganizationId, e.OccurredAt,
            e.ActorId, e.ActorType, e.ActorEmail, e.ActorIp,
            e.Action, e.Module, e.Result, e.FailureReason,
            e.ResourceId, e.ResourceType, e.ResourceName,
            e.CorrelationId)).ToList();

        return Ok(ApiResponse.List(dtos, total, page, pageSize));
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
        var query = platformDb.FeatureFlags.AsQueryable();

        var flags = await query.OrderBy(f => f.Key).ToListAsync(ct);
        return Ok200(flags.Select(f => new
        {
            f.Id, f.Key, f.Description, f.Type, f.DefaultValue, f.Module, f.IsActive, f.CreatedAt
        }).ToList());
    }

    // ── PUT /api/v1/admin/feature-flags/{id} ─────────────────────────────────
    [HttpPut("feature-flags/{id:guid}")]
    public async Task<IActionResult> UpdateFeatureFlag(Guid id, [FromBody] UpdateFeatureFlagRequest req, CancellationToken ct)
    {
        var flag = await platformDb.FeatureFlags.FindAsync([id], ct);
        if (flag == null) return NotFound();

        if (req.IsActive.HasValue) flag.IsActive = req.IsActive.Value;
        if (req.DefaultValue.HasValue) flag.DefaultValue = req.DefaultValue.Value;
        if (req.Description != null) flag.Description = req.Description;

        await platformDb.SaveChangesAsync(ct);
        return Ok200(new { flag.Id, flag.Key, flag.IsActive });
    }
}

// ── Request models locaux ────────────────────────────────────────────────────
public sealed record SuspendTenantRequest(string Reason);
// UpdateFeatureFlagRequest défini dans Application.Platform.Dtos
