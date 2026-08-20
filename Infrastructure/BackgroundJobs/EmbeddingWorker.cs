using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// Vectorise les chunks de connaissance en attente.
///
/// <c>KnowledgeChunk.IsEmbedded</c> et <c>GetPendingEmbeddingAsync</c> existaient
/// déjà dans le modèle, mais rien ne produisait jamais d'embedding : la recherche
/// sémantique était donc structurellement impossible. Ce worker comble ce chaînon.
/// </summary>
public sealed class EmbeddingWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<EmbeddingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker de vectorisation démarré.");

        // Laisser l'application finir son démarrage avant le premier lot.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
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
                logger.LogError(ex, "Erreur dans la boucle de vectorisation.");
            }

            // Lot plein : il reste probablement du travail, on enchaîne sans pause.
            if (processed < BatchSize)
            {
                try { await Task.Delay(IdleInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("Worker de vectorisation arrêté.");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        // Première passe hors tenant : trouver les chunks en attente, tous tenants confondus.
        List<PendingChunk> pending;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();

            pending = await db.KnowledgeChunks
                .IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && !c.IsEmbedded)
                .OrderBy(c => c.CreatedAt)
                .Take(BatchSize)
                .Select(c => new PendingChunk(c.Id, c.OrganizationId))
                .ToListAsync(ct);
        }

        if (pending.Count == 0) return 0;

        // Regrouper par tenant : un scope par organisation, avec son contexte positionné.
        var processed = 0;
        foreach (var group in pending.GroupBy(p => p.OrganizationId))
        {
            if (ct.IsCancellationRequested) break;
            processed += await EmbedForTenantAsync(group.Key, group.Select(g => g.ChunkId).ToList(), ct);
        }

        if (processed > 0)
            logger.LogInformation("{Count} chunk(s) vectorisé(s).", processed);

        return processed;
    }

    private async Task<int> EmbedForTenantAsync(Guid organizationId, List<Guid> chunkIds, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<ITenantContext>().SetTenant(organizationId);

        var db  = sp.GetRequiredService<EaiosDbContext>();
        var llm = sp.GetRequiredService<ILlmService>();

        var model = configuration["Ai:DefaultEmbeddingModel"] ?? "text-embedding-3-large";

        var chunks = await db.KnowledgeChunks
            .Where(c => chunkIds.Contains(c.Id))
            .ToListAsync(ct);

        if (chunks.Count == 0) return 0;

        var texts = chunks.Select(c => c.Content).ToList();

        IReadOnlyList<float[]> vectors;
        try
        {
            vectors = await llm.EmbedBatchAsync(texts, ct);
        }
        catch (Exception ex)
        {
            // Provider indisponible : les chunks restent en attente et seront
            // repris au prochain tour plutôt que marqués vectorisés à tort.
            logger.LogError(ex, "Échec de génération des embeddings pour le tenant {TenantId}.", organizationId);
            return 0;
        }

        if (vectors.Count != chunks.Count)
        {
            logger.LogError("Le fournisseur a renvoyé {Got} vecteur(s) pour {Expected} chunk(s) — lot ignoré.",
                vectors.Count, chunks.Count);
            return 0;
        }

        // Remplacer les embeddings existants du chunk : un contenu re-vectorisé
        // ne doit pas laisser d'ancien vecteur actif qui fausserait la recherche.
        var existing = await db.Embeddings
            .Where(e => e.ChunkId != null && chunkIds.Contains(e.ChunkId.Value) && e.IsActive)
            .ToListAsync(ct);

        foreach (var stale in existing)
            stale.Deactivate();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk  = chunks[i];
            var vector = vectors[i];

            var embedding = Embedding.CreateLocal(
                organizationId,
                sourceType: "KnowledgeChunk",
                sourceId:   chunk.ItemId,
                chunkId:    chunk.Id,
                embeddingModel: model,
                vector:     vector,
                tokenCount: chunk.TokenCount);

            await db.Embeddings.AddAsync(embedding, ct);
            chunk.MarkEmbeddedLocally(model);
        }

        await db.SaveChangesAsync(ct);
        return chunks.Count;
    }

    private sealed record PendingChunk(Guid ChunkId, Guid OrganizationId);
}
