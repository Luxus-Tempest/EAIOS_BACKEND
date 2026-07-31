using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Knowledge;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum KnowledgeItemType { Article, Faq, Procedure, Policy, Glossary, Reference, DataRecord }
public enum KnowledgeItemSource { Manual, AutoExtracted, Connector, Import }
public enum KnowledgeItemStatus { Draft, UnderReview, Published, Archived }
public enum KnowledgePackStatus { Draft, Published, Archived }
public enum KnowledgeRelationSource { Manual, AutoExtracted }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: KnowledgeItem
// Table: org_{id}.knowledge.items
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class KnowledgeItem : TenantEntity
{
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }
    public string? Content { get; private set; }          // Markdown / rich text
    public KnowledgeItemType Type { get; private set; }
    public KnowledgeItemSource Source { get; private set; }
    public KnowledgeItemStatus Status { get; private set; }

    // ── Links ──────────────────────────────────────────────────────────────────
    public Guid? SourceDocumentId { get; private set; }
    public Guid? SourceVersionId { get; private set; }
    public Guid? PackId { get; private set; }

    // ── Location ───────────────────────────────────────────────────────────────
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }

    // ── AI Quality ─────────────────────────────────────────────────────────────
    public bool IsVerifiedByHuman { get; private set; }
    public Guid? VerifiedBy { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public float? ConfidenceScore { get; private set; }

    // ── Publication ────────────────────────────────────────────────────────────
    public DateTime? PublishedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }

    // ── Tags & Language ────────────────────────────────────────────────────────
    public string[] Tags { get; private set; } = [];
    public string Language { get; private set; } = "fr";

    // ── Stats ──────────────────────────────────────────────────────────────────
    public int ViewCount { get; private set; }
    public float? RelevanceScore { get; private set; }

    // ── Relations ──────────────────────────────────────────────────────────────
    public IReadOnlyList<KnowledgeChunk> Chunks { get; private set; } = [];
    public IReadOnlyList<KnowledgeRelation> Relations { get; private set; } = [];

    public static KnowledgeItem Create(Guid organizationId, string title, KnowledgeItemType type,
        KnowledgeItemSource source, Guid createdBy, string? content = null, Guid? sourceDocumentId = null)
    {
        var item = new KnowledgeItem
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            Type = type,
            Source = source,
            Status = KnowledgeItemStatus.Draft,
            Content = content,
            SourceDocumentId = sourceDocumentId
        };
        item.SetOrganizationId(organizationId);
        item.SetCreated(createdBy);
        return item;
    }

    public void Publish(Guid publishedBy)
    {
        Status = KnowledgeItemStatus.Published;
        PublishedAt = DateTime.UtcNow;
        PublishedBy = publishedBy;
    }

    public void Validate(bool isValid, Guid validatedBy)
    {
        if (isValid)
        {
            IsVerifiedByHuman = true;
            VerifiedBy = validatedBy;
            VerifiedAt = DateTime.UtcNow;
        }
        else
        {
            Status = KnowledgeItemStatus.Draft;
        }
    }

    public void Update(string? title, string? content, string? summary, string[]? tags, string? language)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = title.Trim();
        if (content is not null) Content = content;
        if (summary is not null) Summary = summary;
        if (tags is not null) Tags = tags;
        if (!string.IsNullOrWhiteSpace(language)) Language = language;
    }

    public void Archive() => Status = KnowledgeItemStatus.Archived;
    public void SetPack(Guid? packId) => PackId = packId;
    public void IncrementView() => ViewCount++;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: KnowledgeChunk
// Table: org_{id}.knowledge.chunks
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class KnowledgeChunk : TenantEntity
{
    public Guid ItemId { get; private set; }
    public int ChunkIndex { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int TokenCount { get; private set; }
    public int? StartPage { get; private set; }
    public int? EndPage { get; private set; }
    public string? QdrantPointId { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public bool IsEmbedded { get; private set; }
    public DateTime? EmbeddedAt { get; private set; }
    public string? ContextBefore { get; private set; }
    public string? ContextAfter { get; private set; }

    public static KnowledgeChunk Create(Guid organizationId, Guid itemId, int index, string content, int tokenCount)
    {
        var chunk = new KnowledgeChunk
        {
            Id = Guid.CreateVersion7(),
            ItemId = itemId,
            ChunkIndex = index,
            Content = content,
            TokenCount = tokenCount
        };
        chunk.SetOrganizationId(organizationId);
        chunk.SetCreated(null);
        return chunk;
    }

    public void SetEmbedding(string qdrantPointId, string model) { QdrantPointId = qdrantPointId; EmbeddingModel = model; IsEmbedded = true; EmbeddedAt = DateTime.UtcNow; }
    public void SetPageRange(int? startPage, int? endPage) { StartPage = startPage; EndPage = endPage; }
    public void SetContext(string? before, string? after) { ContextBefore = before; ContextAfter = after; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: KnowledgeRelation
// Table: org_{id}.knowledge.relations
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class KnowledgeRelation : TenantEntity
{
    public Guid SourceItemId { get; private set; }
    public Guid TargetItemId { get; private set; }
    public string RelationType { get; private set; } = string.Empty;  // e.g. "supersedes", "related_to", "references"
    public KnowledgeRelationSource Source { get; private set; }
    public float? ConfidenceScore { get; private set; }
    public string? Label { get; private set; }

    public static KnowledgeRelation Create(Guid organizationId, Guid sourceId, Guid targetId,
        string relationType, KnowledgeRelationSource source, Guid? createdBy = null)
    {
        var rel = new KnowledgeRelation
        {
            Id = Guid.CreateVersion7(),
            SourceItemId = sourceId,
            TargetItemId = targetId,
            RelationType = relationType,
            Source = source
        };
        rel.SetOrganizationId(organizationId);
        rel.SetCreated(createdBy);
        return rel;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: KnowledgePack
// Table: org_{id}.knowledge.packs
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class KnowledgePack : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public KnowledgePackStatus Status { get; private set; }
    public string[] Tags { get; private set; } = [];
    public string Language { get; private set; } = "fr";
    public string? CoverImageUrl { get; private set; }
    public bool IsPublic { get; private set; }
    public int ItemCount { get; private set; }
    public string? ExportStorageKey { get; private set; }
    public DateTime? LastExportedAt { get; private set; }
    public Guid OwnerId { get; private set; }

    public static KnowledgePack Create(Guid organizationId, string name, Guid ownerId, string? description = null, bool isPublic = false)
    {
        var pack = new KnowledgePack
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            IsPublic = isPublic,
            Status = KnowledgePackStatus.Draft,
            OwnerId = ownerId
        };
        pack.SetOrganizationId(organizationId);
        pack.SetCreated(ownerId);
        return pack;
    }

    public void Publish() => Status = KnowledgePackStatus.Published;
    public void Archive() => Status = KnowledgePackStatus.Archived;
    public void Update(string? name, string? description, string[]? tags) { if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim(); if (description is not null) Description = description; if (tags is not null) Tags = tags; }
    public void IncrementItemCount() => ItemCount++;
    public void SetExport(string storageKey) { ExportStorageKey = storageKey; LastExportedAt = DateTime.UtcNow; }
}
