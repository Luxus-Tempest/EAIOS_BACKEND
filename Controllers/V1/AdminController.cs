using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Application.Platform;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Controllers.V1;

[Route("api/v1/admin")]
public sealed class AdminController(PlatformDbContext platformDb) : V1ApiController
{
    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var total = await platformDb.Organizations.CountAsync(ct);
        var orgs = await platformDb.Organizations
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = orgs.Select(o => new TenantSummaryDto(
            o.Id, o.Name, o.Slug, o.Status.ToString(), o.PlanId, o.CurrentUsers, o.MaxUsers,
            o.StorageUsedBytes, o.StorageQuotaBytes, o.CreatedAt, o.TrialEndsAt)).ToList();

        return Ok(ApiResponse.List(dtos, total, page, pageSize));
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> ListAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var total = await platformDb.AuditEvents.CountAsync(ct);
        var events = await platformDb.AuditEvents
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = events.Select(e => new AuditEventDto(
            e.Id, e.OrganizationId, e.OccurredAt, e.ActorId, e.ActorType, e.ActorEmail, e.ActorIp,
            e.Action, e.Module, e.Result, e.FailureReason, e.ResourceId, e.ResourceType, e.ResourceName, e.CorrelationId)).ToList();

        return Ok(ApiResponse.List(dtos, total, page, pageSize));
    }
}
