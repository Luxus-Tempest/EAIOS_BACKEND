using EAIOS.Api.Application.Analytics;
using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Domain.Platform;
using EAIOS.Api.Infrastructure.Audit;
using EAIOS.Api.Infrastructure.Email;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Seeds;
using EAIOS.Api.Infrastructure.Security;
using EAIOS.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace EAIOS.Api.Application.Platform;

public sealed class PlatformAdminService(
    PlatformDbContext platformDb,
    EaiosDbContext eaiosDb,
    ITenantContext tenantContext,
    IPasswordService passwordService,
    IEmailService emailService,
    IReportService reportService,
    IAuditService auditService,
    IStorageService storage,
    IConfiguration configuration,
    ILogger<PlatformAdminService> logger) : IPlatformAdminService
{
    // ═════════════════════════════════════════════════════════════════════════
    // TENANTS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(int page, int pageSize, string? query, CancellationToken ct = default)
    {
        var q = platformDb.Organizations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(o => EF.Functions.Like(o.Name, $"%{term}%")
                          || EF.Functions.Like(o.Slug, $"%{term}%"));
        }

        var total = await q.CountAsync(ct);
        var orgs = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<TenantSummaryDto>(orgs.Select(Map).ToList(), page, pageSize, total);
    }

    public async Task<TenantSummaryDto> GetTenantAsync(Guid id, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("Tenant introuvable.");
        return Map(org);
    }

    /// <summary>
    /// Provisionne un tenant complet : organisation, catalogue de permissions et rôles
    /// système, utilisateur administrateur, workspace par défaut et attribution du rôle
    /// org.admin. Sans cette initialisation, un tenant fraîchement créé serait
    /// inutilisable (aucun rôle, aucun compte pour s'y connecter).
    /// </summary>
    public async Task<TenantSummaryDto> CreateTenantAsync(CreateTenantRequest req, Guid actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Le nom de l'organisation est obligatoire.");
        if (string.IsNullOrWhiteSpace(req.Slug))
            throw new ArgumentException("Le slug de l'organisation est obligatoire.");
        if (string.IsNullOrWhiteSpace(req.AdminEmail))
            throw new ArgumentException("L'email de l'administrateur est obligatoire.");

        var slug = req.Slug.Trim().ToLowerInvariant();
        if (await platformDb.Organizations.AnyAsync(o => o.Slug == slug, ct))
            throw new InvalidOperationException($"Le slug « {slug} » est déjà utilisé par une autre organisation.");

        if (!string.IsNullOrWhiteSpace(req.AdminPassword) && !passwordService.IsStrongPassword(req.AdminPassword))
            throw new ArgumentException("Le mot de passe administrateur ne respecte pas les critères de sécurité.");

        // ── 1. L'organisation elle-même (base plateforme, hors tenant) ────────
        var org = Domain.Organization.Organization.Create(req.Name, slug);
        org.PlanId            = string.IsNullOrWhiteSpace(req.PlanId) ? "free" : req.PlanId;
        org.Status            = OrganizationStatus.Active;
        org.MaxUsers          = configuration.GetValue("Provisioning:DefaultMaxUsers", 10);
        org.StorageQuotaBytes = configuration.GetValue("Provisioning:DefaultStorageQuotaBytes", 5L * 1024 * 1024 * 1024);
        org.CurrentUsers      = 1;

        await platformDb.Organizations.AddAsync(org, ct);
        await platformDb.SaveChangesAsync(ct);

        // ── 2. Bascule du contexte tenant vers la nouvelle organisation ───────
        // Le DbContext force OrganizationId depuis ITenantContext au SaveChanges :
        // sans cette bascule, les entités seraient rattachées au tenant de l'admin.
        var previousTenant = tenantContext.IsResolved ? tenantContext.OrganizationId : (Guid?)null;
        tenantContext.SetTenant(org.Id);

        try
        {
            // ── 3. Permissions + rôles système ───────────────────────────────
            await SystemPermissionsSeed.SeedAsync(eaiosDb, org.Id, ct);

            // ── 4. Utilisateur administrateur ────────────────────────────────
            var admin = User.Create(org.Id, req.AdminEmail.Trim(), "Admin", req.Name.Trim());

            if (!string.IsNullOrWhiteSpace(req.AdminPassword))
            {
                admin.SetPasswordHash(passwordService.HashPassword(req.AdminPassword));
                admin.Activate();
            }
            else
            {
                // Sans mot de passe fourni, le compte reste à vérifier et l'admin
                // reçoit un lien de définition de mot de passe.
                admin.SetEmailVerificationToken(Guid.CreateVersion7().ToString("N"));
            }

            await eaiosDb.Users.AddAsync(admin, ct);
            await eaiosDb.SaveChangesAsync(ct);

            // ── 5. Attribution du rôle org.admin ─────────────────────────────
            var adminRole = await eaiosDb.Roles.FirstOrDefaultAsync(r => r.Name == SystemRoles.OrgAdmin, ct);
            if (adminRole is not null)
            {
                var assignment = UserRole.Create(org.Id, admin.Id, adminRole.Id, adminRole.Name, actorId);
                await eaiosDb.UserRoles.AddAsync(assignment, ct);
            }

            // ── 6. Workspace par défaut ──────────────────────────────────────
            var workspace = Workspace.Create(
                org.Id, "Espace général", admin.Id,
                WorkspaceType.Standard,
                "Espace de travail créé automatiquement à la création de l'organisation.");
            await eaiosDb.Workspaces.AddAsync(workspace, ct);

            await eaiosDb.SaveChangesAsync(ct);

            logger.LogInformation("Tenant provisionné : {Slug} ({OrgId}) — admin {Email}",
                org.Slug, org.Id, admin.Email);

            // ── 7. Email d'accueil / vérification ────────────────────────────
            if (admin.EmailVerificationToken is not null)
                await emailService.SendEmailVerificationAsync(admin.Email, admin.FirstName, admin.EmailVerificationToken, ct);
            else
                await emailService.SendWelcomeAsync(admin.Email, admin.FirstName, org.Name, ct);

            await RecordAuditAsync("platform.tenant.created", actorId, org.Id,
                resourceId: org.Id, resourceType: "Organization", resourceName: org.Name, ct: ct);
        }
        catch (Exception ex)
        {
            // Le provisioning est partiel : on retire l'organisation pour ne pas
            // laisser un tenant inutilisable dans la base plateforme.
            logger.LogError(ex, "Échec du provisioning du tenant {OrgId} — annulation.", org.Id);
            platformDb.Organizations.Remove(org);
            await platformDb.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (previousTenant.HasValue) tenantContext.SetTenant(previousTenant.Value);
        }

        return Map(org);
    }

    public async Task<TenantSummaryDto> SuspendTenantAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("Tenant introuvable.");

        org.Status    = OrganizationStatus.Suspended;
        org.Touch();
        await platformDb.SaveChangesAsync(ct);

        // Suspendre sans révoquer les sessions laisserait les utilisateurs actifs
        // continuer à travailler jusqu'à expiration de leur token.
        var revoked = await RevokeAllSessionsAsync(id, "tenant_suspended", ct);

        await RecordAuditAsync("platform.tenant.suspended", actorId, id,
            resourceId: id, resourceType: "Organization", resourceName: org.Name,
            failureReason: reason, ct: ct);

        logger.LogWarning("Tenant {OrgId} suspendu ({Reason}) — {Count} session(s) révoquée(s).", id, reason, revoked);
        return Map(org);
    }

    public async Task<TenantSummaryDto> ReactivateTenantAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("Tenant introuvable.");

        org.Status    = OrganizationStatus.Active;
        org.Touch();
        await platformDb.SaveChangesAsync(ct);

        await RecordAuditAsync("platform.tenant.reactivated", actorId, id,
            resourceId: id, resourceType: "Organization", resourceName: org.Name, ct: ct);

        logger.LogInformation("Tenant {OrgId} réactivé.", id);
        return Map(org);
    }

    /// <summary>Statistiques d'usage réelles du tenant, lues dans sa propre base.</summary>
    public async Task<object> GetTenantStatsAsync(Guid id, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException("Tenant introuvable.");

        var previousTenant = tenantContext.IsResolved ? tenantContext.OrganizationId : (Guid?)null;
        tenantContext.SetTenant(id);

        try
        {
            var users        = await eaiosDb.Users.CountAsync(ct);
            var activeUsers  = await eaiosDb.Users.CountAsync(u => u.Status == UserStatus.Active, ct);
            var documents    = await eaiosDb.Documents.CountAsync(ct);
            var storageBytes = await eaiosDb.Documents.SumAsync(d => (long?)d.FileSizeBytes, ct) ?? 0L;
            var workspaces   = await eaiosDb.Workspaces.CountAsync(ct);
            var departments  = await eaiosDb.Departments.CountAsync(ct);
            var agents       = await eaiosDb.Agents.CountAsync(ct);
            var executions   = await eaiosDb.AgentExecutions.CountAsync(ct);
            var aiCost       = await eaiosDb.AgentExecutions.SumAsync(e => (decimal?)e.CostUsd, ct) ?? 0m;
            var tokens       = await eaiosDb.AgentExecutions.SumAsync(e => (long?)e.TotalTokens, ct) ?? 0L;
            var workflows    = await eaiosDb.WorkflowDefinitions.CountAsync(ct);
            var instances    = await eaiosDb.WorkflowInstances.CountAsync(ct);
            var knowledge    = await eaiosDb.KnowledgeItems.CountAsync(ct);
            var lastActivity = await eaiosDb.Users.MaxAsync(u => (DateTime?)u.LastLoginAt, ct);

            return new
            {
                org.Id,
                org.Name,
                org.Slug,
                Status = org.Status.ToString(),
                org.PlanId,
                Users = new
                {
                    Total   = users,
                    Active  = activeUsers,
                    Max     = org.MaxUsers,
                    Percent = org.MaxUsers == 0 ? 0d : Math.Round(100d * users / org.MaxUsers, 1)
                },
                Storage = new
                {
                    UsedBytes  = storageBytes,
                    QuotaBytes = org.StorageQuotaBytes,
                    UsedGb     = Math.Round(storageBytes / 1024d / 1024d / 1024d, 3),
                    Percent    = org.StorageQuotaBytes == 0 ? 0d : Math.Round(100d * storageBytes / org.StorageQuotaBytes, 1)
                },
                Content = new { Documents = documents, KnowledgeItems = knowledge, Workspaces = workspaces, Departments = departments },
                Ai = new
                {
                    Agents            = agents,
                    Executions        = executions,
                    TotalCostUsd      = aiCost,
                    TotalTokens       = tokens,
                    MonthlyTokenQuota = org.MonthlyTokenQuota
                },
                Workflows    = new { Definitions = workflows, Instances = instances },
                LastActivity = lastActivity,
                org.CreatedAt,
                org.TrialEndsAt
            };
        }
        finally
        {
            if (previousTenant.HasValue) tenantContext.SetTenant(previousTenant.Value);
        }
    }

    public async Task<TenantSummaryDto> UpdateTenantLicenseAsync(Guid id, UpdateLicenseRequest req, Guid actorId, CancellationToken ct = default)
    {
        var org = await platformDb.Organizations.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("Tenant introuvable.");

        org.PlanId            = req.PlanId;
        org.MaxUsers          = req.MaxUsers;
        org.StorageQuotaBytes = req.StorageQuotaBytes;
        org.TrialEndsAt       = req.TrialEndsAt;
        org.Touch();

        await platformDb.SaveChangesAsync(ct);

        await RecordAuditAsync("platform.tenant.license_updated", actorId, id,
            resourceId: id, resourceType: "Organization", resourceName: org.Name, ct: ct);

        return Map(org);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FEATURE FLAGS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<FeatureFlagDto>> ListFeatureFlagsAsync(Guid? organizationId, CancellationToken ct = default)
    {
        var flags = await platformDb.FeatureFlags
            .AsNoTracking()
            .Include(f => f.Overrides)
            .OrderBy(f => f.Key)
            .ToListAsync(ct);

        return flags.Select(f =>
        {
            // Filtré sur une organisation : on ne remonte que l'override qui la concerne.
            var overrides = (organizationId.HasValue
                    ? f.Overrides.Where(o => o.OrganizationId == organizationId.Value)
                    : f.Overrides)
                .Select(o => new FeatureFlagOverrideDto(o.Id, o.OrganizationId, o.Value, o.Reason, o.ExpiresAt))
                .ToList();

            return new FeatureFlagDto(f.Id, f.Key, f.Description, f.Type, f.DefaultValue, f.Module, f.IsActive, overrides);
        }).ToList();
    }

    public async Task<FeatureFlagDto> UpdateFeatureFlagAsync(Guid id, UpdateFeatureFlagRequest req, Guid actorId, CancellationToken ct = default)
    {
        var flag = await platformDb.FeatureFlags
            .Include(f => f.Overrides)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException("Feature flag introuvable.");

        if (req.IsActive.HasValue)     flag.IsActive     = req.IsActive.Value;
        if (req.DefaultValue.HasValue) flag.DefaultValue = req.DefaultValue.Value;
        if (req.Description is not null) flag.Description = req.Description;
        flag.Touch();

        await platformDb.SaveChangesAsync(ct);

        await RecordAuditAsync("platform.feature_flag.updated", actorId, null,
            resourceId: flag.Id, resourceType: "FeatureFlag", resourceName: flag.Key, ct: ct);

        var overrides = flag.Overrides
            .Select(o => new FeatureFlagOverrideDto(o.Id, o.OrganizationId, o.Value, o.Reason, o.ExpiresAt))
            .ToList();

        return new FeatureFlagDto(flag.Id, flag.Key, flag.Description, flag.Type, flag.DefaultValue, flag.Module, flag.IsActive, overrides);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // AUDIT
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<PagedResult<AuditEventDto>> ListAuditLogsAsync(
        Guid? organizationId, string? action, string? actorId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = platformDb.AuditEvents.AsNoTracking().AsQueryable();

        if (organizationId.HasValue)
            query = query.Where(e => e.OrganizationId == organizationId.Value);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(e => EF.Functions.Like(e.Action, $"%{action.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(actorId) && Guid.TryParse(actorId, out var actorGuid))
            query = query.Where(e => e.ActorId == actorGuid);

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

    /// <summary>
    /// Délègue l'export au pipeline de rapports asynchrones : un ReportJob de type
    /// « audit » est créé, généré hors requête puis téléchargeable. Retourne l'identifiant
    /// du job à scruter.
    /// </summary>
    public async Task<string> ExportAuditLogsAsync(Guid actorId, CancellationToken ct = default)
    {
        if (!tenantContext.IsResolved)
            throw new InvalidOperationException("Un tenant doit être résolu pour exporter les journaux d'audit.");

        var request = new GenerateReportRequest(
            ReportType: "audit",
            DateFrom:   DateTime.UtcNow.AddDays(-90),
            DateTo:     DateTime.UtcNow,
            Format:     "csv");

        var job = await reportService.RequestAsync(tenantContext.OrganizationId, request, actorId, ct);

        logger.LogInformation("Export des journaux d'audit demandé — job {JobId}", job.Id);
        return job.Id.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DIAGNOSTICS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Sonde réellement chaque dépendance plutôt que de renvoyer « Healthy » en dur.</summary>
    public async Task<object> GetHealthStatusAsync(CancellationToken ct = default)
    {
        var checks = new List<object>();
        var healthy = true;

        // ── Base de données plateforme ────────────────────────────────────────
        var sw = Stopwatch.StartNew();
        try
        {
            var canConnect = await platformDb.Database.CanConnectAsync(ct);
            sw.Stop();
            healthy &= canConnect;
            checks.Add(new
            {
                Component = "database.platform",
                Status    = canConnect ? "Healthy" : "Unhealthy",
                LatencyMs = sw.ElapsedMilliseconds,
                Provider  = platformDb.Database.ProviderName
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            healthy = false;
            checks.Add(new { Component = "database.platform", Status = "Unhealthy", LatencyMs = sw.ElapsedMilliseconds, Error = ex.Message });
        }

        // ── Base de données tenant ────────────────────────────────────────────
        sw.Restart();
        try
        {
            var canConnect = await eaiosDb.Database.CanConnectAsync(ct);
            sw.Stop();
            healthy &= canConnect;
            checks.Add(new
            {
                Component = "database.tenant",
                Status    = canConnect ? "Healthy" : "Unhealthy",
                LatencyMs = sw.ElapsedMilliseconds,
                Provider  = eaiosDb.Database.ProviderName
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            healthy = false;
            checks.Add(new { Component = "database.tenant", Status = "Unhealthy", LatencyMs = sw.ElapsedMilliseconds, Error = ex.Message });
        }

        // ── Stockage objet ────────────────────────────────────────────────────
        sw.Restart();
        try
        {
            // Une clé volontairement inexistante : on teste l'accessibilité du backend,
            // pas la présence d'un objet particulier.
            await storage.ExistsAsync("__healthcheck__/probe", ct);
            sw.Stop();
            checks.Add(new
            {
                Component = "storage",
                Status    = "Healthy",
                LatencyMs = sw.ElapsedMilliseconds,
                Provider  = configuration["Storage:Provider"] ?? "Local"
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            healthy = false;
            checks.Add(new { Component = "storage", Status = "Unhealthy", LatencyMs = sw.ElapsedMilliseconds, Error = ex.Message });
        }

        var process = Process.GetCurrentProcess();

        return new
        {
            Status      = healthy ? "Healthy" : "Degraded",
            Version     = typeof(PlatformAdminService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            Timestamp   = DateTime.UtcNow,
            UptimeSeconds = Math.Round((DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds, 0),
            Checks      = checks
        };
    }

    /// <summary>Métriques réelles du processus et volumétrie de la plateforme.</summary>
    public async Task<object> GetMetricsAsync(CancellationToken ct = default)
    {
        var process = Process.GetCurrentProcess();
        var uptime  = DateTime.UtcNow - process.StartTime.ToUniversalTime();

        var totalTenants     = await platformDb.Organizations.CountAsync(ct);
        var activeTenants    = await platformDb.Organizations.CountAsync(o => o.Status == OrganizationStatus.Active, ct);
        var suspendedTenants = await platformDb.Organizations.CountAsync(o => o.Status == OrganizationStatus.Suspended, ct);

        var since24h    = DateTime.UtcNow.AddHours(-24);
        var auditLast24 = await platformDb.AuditEvents.CountAsync(e => e.OccurredAt >= since24h, ct);
        var auditFailed = await platformDb.AuditEvents.CountAsync(
            e => e.OccurredAt >= since24h && e.Result == AuditEventResult.Failure, ct);

        return new
        {
            Timestamp = DateTime.UtcNow,
            Process = new
            {
                UptimeSeconds       = Math.Round(uptime.TotalSeconds, 0),
                WorkingSetBytes     = process.WorkingSet64,
                PrivateMemoryBytes  = process.PrivateMemorySize64,
                ManagedHeapBytes    = GC.GetTotalMemory(forceFullCollection: false),
                TotalProcessorSeconds = Math.Round(process.TotalProcessorTime.TotalSeconds, 2),
                CpuPercentSinceStart = uptime.TotalSeconds <= 0
                    ? 0d
                    : Math.Round(100d * process.TotalProcessorTime.TotalSeconds
                                 / (uptime.TotalSeconds * Environment.ProcessorCount), 2),
                ThreadCount   = process.Threads.Count,
                HandleCount   = process.HandleCount,
                ProcessorCount = Environment.ProcessorCount,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2)
            },
            Platform = new
            {
                TotalTenants     = totalTenants,
                ActiveTenants    = activeTenants,
                SuspendedTenants = suspendedTenants
            },
            Audit = new
            {
                EventsLast24h        = auditLast24,
                FailedEventsLast24h  = auditFailed,
                EventsPerMinuteLast24h = Math.Round(auditLast24 / 1440d, 3)
            },
            Runtime = new
            {
                Framework    = Environment.Version.ToString(),
                OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
            }
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Révoque toutes les sessions actives d'un tenant. Renvoie le nombre révoqué.</summary>
    private async Task<int> RevokeAllSessionsAsync(Guid organizationId, string reason, CancellationToken ct)
    {
        // IgnoreQueryFilters : l'admin plateforme agit sur un tenant qui n'est pas le sien.
        var sessions = await eaiosDb.Sessions
            .IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId
                     && !s.IsDeleted
                     && s.Status == SessionStatus.Active)
            .ToListAsync(ct);

        foreach (var session in sessions)
            session.Revoke(reason);

        if (sessions.Count > 0)
            await eaiosDb.SaveChangesAsync(ct);

        return sessions.Count;
    }

    private Task RecordAuditAsync(
        string action, Guid actorId, Guid? organizationId,
        Guid? resourceId = null, string? resourceType = null, string? resourceName = null,
        string? failureReason = null, CancellationToken ct = default) =>
        // IAuditService avale déjà ses propres erreurs : l'audit ne peut pas
        // faire échouer l'action administrative.
        auditService.LogAsync(
            organizationId: organizationId ?? (tenantContext.IsResolved ? tenantContext.OrganizationId : Guid.Empty),
            action:         action,
            actorType:      "User",
            result:         failureReason is null ? AuditEventResult.Success : AuditEventResult.Failure,
            actorId:        actorId,
            resourceId:     resourceId,
            resourceType:   resourceType,
            resourceName:   resourceName,
            module:         "platform",
            failureReason:  failureReason,
            ct:             ct);

    private static TenantSummaryDto Map(Domain.Organization.Organization o) => new(
        o.Id, o.Name, o.Slug, o.Status.ToString(), o.PlanId,
        o.CurrentUsers, o.MaxUsers, o.StorageUsedBytes, o.StorageQuotaBytes,
        o.CreatedAt, o.TrialEndsAt);
}
