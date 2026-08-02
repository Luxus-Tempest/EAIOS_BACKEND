using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Platform;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Application.Platform;

public sealed class PlatformAdminService(
    PlatformDbContext platformDb) : IPlatformAdminService
{
    public async Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(int page, int pageSize, string? query, CancellationToken ct = default)
    {
        var q = platformDb.Organizations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(o => o.Name.Contains(query) || o.Slug.Contains(query));

        var total = await q.CountAsync(ct);
        var orgs = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = orgs.Select(Map).ToList();
        return new PagedResult<TenantSummaryDto>(dtos, page, pageSize, total);
    }

    public async Task<TenantSummaryDto> GetTenantAsync(Guid id, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct) ?? throw new KeyNotFoundException("Tenant introuvable.");
        return Map(org);
    }

    public async Task<TenantSummaryDto> CreateTenantAsync(CreateTenantRequest req, Guid actorId, CancellationToken ct = default)
    {
        // Simplification : Création de l'entité Organization
        var org = EAIOS.Api.Domain.Organization.Organization.Create(req.Name, req.Slug);
        org.PlanId = req.PlanId;
        org.Status = OrganizationStatus.Active;
        org.MaxUsers = 10;
        org.StorageQuotaBytes = 1024L * 1024 * 1024 * 5;
        
        // En production: Création de l'utilisateur Admin initial, du Workspace par défaut, des permissions, etc.
        // Cela nécessiterait une transaction répartie ou l'appel à des services de domaine pour initialiser le tenant.
        
        await platformDb.Organizations.AddAsync(org, ct);
        await platformDb.SaveChangesAsync(ct);
        
        return Map(org);
    }

    public async Task<TenantSummaryDto> SuspendTenantAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct) ?? throw new KeyNotFoundException("Tenant introuvable.");
        org.Status = OrganizationStatus.Suspended;
        
        // Logique d'audit (ex: enregistrer "reason") à ajouter en production.
        
        await platformDb.SaveChangesAsync(ct);
        return Map(org);
    }

    public async Task<TenantSummaryDto> ReactivateTenantAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct) ?? throw new KeyNotFoundException("Tenant introuvable.");
        org.Status = OrganizationStatus.Active;
        await platformDb.SaveChangesAsync(ct);
        return Map(org);
    }

    public async Task<object> GetTenantStatsAsync(Guid id, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct) ?? throw new KeyNotFoundException("Tenant introuvable.");
        return new
        {
            org.Id,
            org.Name,
            org.CurrentUsers,
            org.MaxUsers,
            org.StorageUsedBytes,
            org.StorageQuotaBytes
        };
    }

    public async Task<TenantSummaryDto> UpdateTenantLicenseAsync(Guid id, UpdateLicenseRequest req, Guid actorId, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct) ?? throw new KeyNotFoundException("Tenant introuvable.");
        
        org.PlanId = req.PlanId;
        org.MaxUsers = req.MaxUsers;
        org.StorageQuotaBytes = req.StorageQuotaBytes;
        org.TrialEndsAt = req.TrialEndsAt;
        
        await platformDb.SaveChangesAsync(ct);
        return Map(org);
    }

    public async Task<IReadOnlyList<FeatureFlagDto>> ListFeatureFlagsAsync(Guid? organizationId, CancellationToken ct = default)
    {
        var query = platformDb.FeatureFlags.AsQueryable();
        // Si organizationId est fourni, on pourrait filtrer les overrides, etc.
        var flags = await query.OrderBy(f => f.Key).ToListAsync(ct);
        
        return flags.Select(f => new FeatureFlagDto(
            f.Id, f.Key, f.Description, f.Type, f.DefaultValue, f.Module, f.IsActive, new List<FeatureFlagOverrideDto>()
        )).ToList();
    }

    public async Task<FeatureFlagDto> UpdateFeatureFlagAsync(Guid id, UpdateFeatureFlagRequest req, Guid actorId, CancellationToken ct = default)
    {
        var flag = await platformDb.FeatureFlags.FindAsync([id], ct) ?? throw new KeyNotFoundException("Feature flag introuvable.");
        
        if (req.IsActive.HasValue) flag.IsActive = req.IsActive.Value;
        if (req.DefaultValue.HasValue) flag.DefaultValue = req.DefaultValue.Value;
        if (req.Description != null) flag.Description = req.Description;
        
        await platformDb.SaveChangesAsync(ct);
        
        return new FeatureFlagDto(
            flag.Id, flag.Key, flag.Description, flag.Type, flag.DefaultValue, flag.Module, flag.IsActive, new List<FeatureFlagOverrideDto>()
        );
    }

    public async Task<PagedResult<AuditEventDto>> ListAuditLogsAsync(Guid? organizationId, string? action, string? actorId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = platformDb.AuditEvents.AsQueryable();

        if (organizationId.HasValue)
            query = query.Where(e => e.OrganizationId == organizationId.Value);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(e => e.Action.Contains(action));
        if (!string.IsNullOrWhiteSpace(actorId))
            query = query.Where(e => e.ActorId.ToString() == actorId);

        var total = await query.CountAsync(ct);
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

        return new PagedResult<AuditEventDto>(dtos, page, pageSize, total);
    }

    public async Task<string> ExportAuditLogsAsync(Guid actorId, CancellationToken ct = default)
    {
        // Stub : en production, lancer un job d'export asynchrone vers S3/Blob et renvoyer un ID de job
        var exportId = Guid.CreateVersion7().ToString("N");
        return await Task.FromResult(exportId);
    }

    public async Task<object> GetHealthStatusAsync(CancellationToken ct = default)
    {
        // Stub
        return await Task.FromResult(new { Status = "Healthy", Version = "1.0.0", Timestamp = DateTime.UtcNow });
    }

    public async Task<object> GetMetricsAsync(CancellationToken ct = default)
    {
        // Stub
        return await Task.FromResult(new { RequestsPerSecond = 42, ActiveConnections = 12, CpuUsage = 15.5 });
    }

    private static TenantSummaryDto Map(EAIOS.Api.Domain.Organization.Organization o) => new(
        o.Id, o.Name, o.Slug, o.Status.ToString(), o.PlanId,
        o.CurrentUsers, o.MaxUsers, o.StorageUsedBytes, o.StorageQuotaBytes,
        o.CreatedAt, o.TrialEndsAt);
}
