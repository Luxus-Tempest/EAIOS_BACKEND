using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Application.Resource;
using EAIOS.Api.Domain.Platform;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Audit;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Applique la rétention documentaire.
///
/// <para>
/// Le modèle portait une date d'expiration et un drapeau de conservation
/// légale, et rien ne les appliquait : la corbeille annonçait une purge à
/// trente jours que personne n'exécutait. Ce worker fait deux choses, chaque
/// jour et au démarrage :
/// </para>
/// <list type="number">
///   <item>un document actif dont la rétention est échue part à la corbeille ;</item>
///   <item>un document en corbeille depuis plus de <c>Retention:TrashPurgeDays</c> jours est purgé — fichiers compris.</item>
/// </list>
/// <para>Une conservation légale suspend les deux, sans exception. Chaque geste est journalisé.</para>
/// </summary>
public sealed class RetentionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public static int TrashPurgeDays(IConfiguration configuration) =>
        Math.Max(1, configuration.GetValue("Retention:TrashPurgeDays", 30));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur dans la passe de rétention.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var purgeBefore = now.AddDays(-TrashPurgeDays(configuration));

        List<(Guid Id, Guid OrganizationId, bool Expired)> candidates;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
            candidates = await db.Documents
                .IgnoreQueryFilters()
                .Where(d => !d.IsDeleted && !d.HasLegalHold
                         && ((d.Status == ResourceStatus.Active && d.RetentionExpiresAt != null && d.RetentionExpiresAt < now)
                          || (d.Status == ResourceStatus.Trashed && d.UpdatedAt < purgeBefore)))
                .Select(d => new ValueTuple<Guid, Guid, bool>(d.Id, d.OrganizationId, d.Status == ResourceStatus.Active))
                .ToListAsync(ct);
        }

        if (candidates.Count == 0) return;
        logger.LogInformation("Rétention : {Count} document(s) à traiter.", candidates.Count);

        foreach (var (id, organizationId, expired) in candidates)
        {
            if (ct.IsCancellationRequested) break;

            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(organizationId);
            var documents = sp.GetRequiredService<IDocumentService>();
            var audit = sp.GetRequiredService<IAuditService>();

            try
            {
                if (expired)
                {
                    await documents.DeleteDocumentAsync(id, ct);
                    await audit.LogAsync(organizationId, "document.retention_expired", "System",
                        AuditEventResult.Success, resourceId: id, resourceType: "Document", module: "Resource", ct: ct);
                }
                else
                {
                    await documents.PurgeDocumentAsync(id, ct);
                    await audit.LogAsync(organizationId, "document.purged_by_retention", "System",
                        AuditEventResult.Success, resourceId: id, resourceType: "Document", module: "Resource", ct: ct);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message == "LEGAL_HOLD_ACTIVE")
            {
                // Le drapeau et la table peuvent diverger : la table fait foi.
                logger.LogInformation("Document {DocumentId} : conservation légale active, rétention suspendue.", id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rétention du document {DocumentId} en échec.", id);
            }
        }
    }
}
