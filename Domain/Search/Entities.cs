using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Search;

public enum SearchType { Basic, Advanced, Semantic, Hybrid }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: SavedSearch
// Table: org_{id}.search.saved_searches
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class SavedSearch : TenantEntity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string QueryText { get; private set; } = string.Empty;
    public string FiltersJson { get; private set; } = "{}";
    public SearchType SearchType { get; private set; }
    public bool AlertEnabled { get; private set; }
    public string? AlertFrequency { get; private set; }
    public int UsageCount { get; private set; }
    public DateTime? LastExecutedAt { get; private set; }
    public bool IsShared { get; private set; }

    public static SavedSearch Create(Guid organizationId, Guid userId, string name,
        string queryText, SearchType searchType, string? filtersJson = null,
        bool alertEnabled = false, string? alertFrequency = null)
    {
        var s = new SavedSearch
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = name.Trim(),
            QueryText = queryText,
            SearchType = searchType,
            FiltersJson = filtersJson ?? "{}",
            AlertEnabled = alertEnabled,
            AlertFrequency = alertFrequency
        };
        s.SetOrganizationId(organizationId);
        s.SetCreated(userId);
        return s;
    }

    public void RecordExecution() { UsageCount++; LastExecutedAt = DateTime.UtcNow; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Embedding (metadata only — vector in Qdrant)
// Table: org_{id}.search.embeddings
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Embedding : TenantEntity
{
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public Guid? ChunkId { get; private set; }
    public string EmbeddingModel { get; private set; } = string.Empty;
    public int Dimensions { get; private set; }
    public string QdrantPointId { get; private set; } = string.Empty;
    public string QdrantCollectionName { get; private set; } = string.Empty;
    public int TokenCount { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public bool IsActive { get; private set; }

    public static Embedding Create(Guid organizationId, string sourceType, Guid sourceId,
        string embeddingModel, int dimensions, string qdrantPointId, string collectionName, int tokenCount)
    {
        var e = new Embedding
        {
            Id = Guid.CreateVersion7(),
            SourceType = sourceType,
            SourceId = sourceId,
            EmbeddingModel = embeddingModel,
            Dimensions = dimensions,
            QdrantPointId = qdrantPointId,
            QdrantCollectionName = collectionName,
            TokenCount = tokenCount,
            GeneratedAt = DateTime.UtcNow,
            IsActive = true
        };
        e.SetOrganizationId(organizationId);
        e.SetCreated(null);
        return e;
    }

    public void Deactivate() => IsActive = false;
}
