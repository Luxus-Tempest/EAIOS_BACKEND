using EAIOS.Api.Domain.Search;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.AI;

/// <summary>
/// Recherche vectorielle sur les embeddings stockés en base.
///
/// Le modèle de domaine prévoyait Qdrant, mais aucune base vectorielle n'est
/// déployée : les vecteurs vivent donc dans <c>search.embeddings</c> et la
/// similarité cosinus est calculée applicativement. Suffisant aux volumes
/// d'un tenant ; le jour où Qdrant arrive, seule cette classe change.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>Chunks de connaissance les plus proches sémantiquement de la question.</summary>
    Task<IReadOnlyList<SemanticHit>> SearchChunksAsync(
        string query, int topK = 5, float minScore = 0.0f, Guid? packId = null, CancellationToken ct = default);
}

public sealed record SemanticHit(
    Guid ChunkId,
    Guid ItemId,
    string ItemTitle,
    string Content,
    float Score);

public sealed class VectorSearchService(
    EaiosDbContext db,
    ILlmService llm,
    ILogger<VectorSearchService> logger) : IVectorSearchService
{
    /// <summary>Plafond de vecteurs chargés en mémoire pour un seul calcul de similarité.</summary>
    private const int MaxVectorsScanned = 20_000;

    public async Task<IReadOnlyList<SemanticHit>> SearchChunksAsync(
        string query, int topK = 5, float minScore = 0.0f, Guid? packId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        topK = Math.Clamp(topK, 1, 50);

        var queryVector = await llm.EmbedAsync(query, ct);
        if (queryVector.Length == 0) return [];

        var embeddings = await db.Embeddings
            .Where(e => e.IsActive && e.SourceType == "KnowledgeChunk" && e.ChunkId != null)
            .Select(e => new { e.ChunkId, e.Vector })
            .Take(MaxVectorsScanned)
            .ToListAsync(ct);

        if (embeddings.Count == 0)
            return [];

        // Ne comparer que des vecteurs de même dimension : un changement de modèle
        // d'embedding laisse d'anciens vecteurs incompatibles en base.
        var scored = embeddings
            .Where(e => e.Vector.Length == queryVector.Length)
            .Select(e => new { e.ChunkId, Score = CosineSimilarity(queryVector, e.Vector) })
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        if (scored.Count == 0) return [];

        var chunkIds = scored.Select(x => x.ChunkId!.Value).ToList();

        var chunks = await db.KnowledgeChunks
            .Where(c => chunkIds.Contains(c.Id))
            .Join(db.KnowledgeItems, c => c.ItemId, i => i.Id,
                  (c, i) => new { c.Id, c.ItemId, ItemTitle = i.Title, c.Content, ItemPackId = i.PackId })
            .ToListAsync(ct);

        if (packId.HasValue)
            chunks = chunks.Where(c => c.ItemPackId == packId.Value).ToList();

        var scoreById = scored.ToDictionary(x => x.ChunkId!.Value, x => x.Score);

        return chunks
            .Select(c => new SemanticHit(c.Id, c.ItemId, c.ItemTitle, c.Content, scoreById.GetValueOrDefault(c.Id)))
            .OrderByDescending(h => h.Score)
            .ToList();
    }

    /// <summary>
    /// Similarité cosinus. Renvoie 0 si l'un des vecteurs est nul, pour éviter
    /// une division par zéro et un score NaN qui casserait le tri.
    /// </summary>
    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        double dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0) return 0f;

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
