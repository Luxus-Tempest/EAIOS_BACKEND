using EAIOS.Api.Application.Analytics;
using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Analytics;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Réveille le worker dès qu'un rapport est demandé, sans attendre le prochain
/// tour de scrutation. Le scrutin périodique reste le filet de sécurité (jobs
/// laissés en attente par un redémarrage).
/// </summary>
public sealed class ReportQueueSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Notify()
    {
        // Ne jamais dépasser 1 : un seul réveil suffit à drainer toute la file.
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        try   { await _signal.WaitAsync(timeout, ct); }
        catch (OperationCanceledException) { /* arrêt demandé */ }
    }
}

/// <summary>
/// Génère les rapports en file d'attente hors du cycle requête/réponse.
/// Chaque job est traité dans son propre scope DI avec le tenant positionné,
/// pour que les Global Query Filters bornent correctement les données extraites.
/// </summary>
public sealed class ReportGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ReportQueueSignal signal,
    ILogger<ReportGenerationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker de génération de rapports démarré.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainQueueAsync(stoppingToken);

                // Si le lot était plein, il reste probablement du travail : on enchaîne.
                if (processed < BatchSize)
                    await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur dans la boucle du worker de rapports — nouvelle tentative dans {Delay}s.",
                    PollInterval.TotalSeconds);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Worker de génération de rapports arrêté.");
    }

    private async Task<int> DrainQueueAsync(CancellationToken ct)
    {
        // Première passe : on cherche les jobs en attente TOUS tenants confondus,
        // d'où le IgnoreQueryFilters — le worker n'a pas de tenant ambiant.
        List<PendingJob> pending;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
            pending = await db.ReportJobs
                .IgnoreQueryFilters()
                .Where(r => !r.IsDeleted && r.Status == ReportJobStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .Take(BatchSize)
                .Select(r => new PendingJob(r.Id, r.OrganizationId))
                .ToListAsync(ct);
        }

        foreach (var (jobId, orgId) in pending)
        {
            if (ct.IsCancellationRequested) break;

            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            // Positionner le tenant AVANT de résoudre le DbContext : les filtres
            // globaux capturent l'ITenantContext du scope courant.
            sp.GetRequiredService<ITenantContext>().SetTenant(orgId);

            try
            {
                await sp.GetRequiredService<IReportService>().ProcessAsync(jobId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec du traitement du rapport {ReportId} (tenant {TenantId})", jobId, orgId);
            }
        }

        return pending.Count;
    }

    private sealed record PendingJob(Guid JobId, Guid OrgId);
}

/// <summary>
/// Purge les rapports générés dont la date d'expiration est dépassée :
/// supprime le fichier du stockage et marque le job comme expiré.
/// Sans cela, les exports s'accumuleraient indéfiniment.
/// </summary>
public sealed class ReportRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReportRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisser l'application démarrer avant la première purge.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec de la purge des rapports expirés.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var expired = await db.ReportJobs
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted
                     && r.Status == ReportJobStatus.Completed
                     && r.ExpiresAt < DateTime.UtcNow)
            .Take(200)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var job in expired)
        {
            if (!string.IsNullOrWhiteSpace(job.StorageKey))
            {
                try
                {
                    await storage.DeleteAsync(job.StorageKey, ct);
                }
                catch (Exception ex)
                {
                    // Le fichier peut avoir déjà disparu : on marque quand même le job expiré.
                    logger.LogWarning(ex, "Impossible de supprimer le fichier du rapport {ReportId}", job.Id);
                }
            }
            job.MarkExpired();
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Count} rapport(s) expiré(s) purgé(s).", expired.Count);
    }
}
