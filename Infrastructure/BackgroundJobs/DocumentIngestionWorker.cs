using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Application.Knowledge;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Extrait le texte des versions déposées, en tâche de fond.
///
/// <para>
/// Un dépôt répond tout de suite ; la lecture du fichier vient après. Le worker
/// balaie les versions encore « déposées » ou « à analyser », tous tenants
/// confondus, puis traite chacune dans le contexte de son organisation — le
/// filtre de tenant capture le contexte du scope courant, il doit donc être
/// positionné <b>avant</b> de résoudre le service.
/// </para>
/// </summary>
public sealed class DocumentIngestionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentIngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 10;

    /// <summary>Signal facultatif : un dépôt peut réveiller le worker sans attendre le prochain balayage.</summary>
    public static readonly SemaphoreSlim Wakeup = new(0, int.MaxValue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker d'extraction de texte démarré.");

        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur dans la boucle d'extraction.");
            }

            if (processed < BatchSize)
            {
                try { await Wakeup.WaitAsync(IdleInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("Worker d'extraction de texte arrêté.");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        List<Pending> pending;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
            pending = await db.DocumentVersions
                .IgnoreQueryFilters()
                .Where(v => !v.IsDeleted && v.IsCurrent
                         && (v.Status == DocumentVersionStatus.Uploaded
                          || v.Status == DocumentVersionStatus.PendingScan
                          || v.Status == DocumentVersionStatus.PendingParsing))
                .OrderBy(v => v.CreatedAt)
                .Take(BatchSize)
                .Select(v => new Pending(v.Id, v.OrganizationId))
                .ToListAsync(ct);
        }

        foreach (var (versionId, organizationId) in pending)
        {
            if (ct.IsCancellationRequested) break;

            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(organizationId);

            try
            {
                var outcome = await sp.GetRequiredService<IDocumentIngestionService>().IngestVersionAsync(versionId, ct);
                if (!outcome.Extracted)
                    logger.LogInformation("Version {VersionId} non extraite : {Reason}", versionId, outcome.Reason);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec de l'extraction de la version {VersionId} (tenant {TenantId})", versionId, organizationId);
                await MarkFailedAsync(versionId, organizationId, ct);
            }
        }

        return pending.Count;
    }

    /// <summary>Une version qui échoue ne doit pas être reprise à chaque balayage : on la sort de la file.</summary>
    private async Task MarkFailedAsync(Guid versionId, Guid organizationId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(organizationId);
        var db = sp.GetRequiredService<EaiosDbContext>();

        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return;
        version.MarkParsed(null, null, null, false);
        version.MarkIndexed(null);

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == version.DocumentId, ct);
        document?.SetIndexingFailed();

        await db.SaveChangesAsync(ct);
    }

    private sealed record Pending(Guid VersionId, Guid OrganizationId);
}
