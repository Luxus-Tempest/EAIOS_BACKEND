using EAIOS.Api.Domain.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

// ── KnowledgeItem ─────────────────────────────────────────────────────────────

public sealed record KnowledgeItemDto(
    Guid Id,
    string Title,
    string? Summary,
    string? Content,
    KnowledgeItemType Type,
    KnowledgeItemSource Source,
    KnowledgeItemStatus Status,
    Guid? SourceDocumentId,
    Guid? PackId,
    Guid? WorkspaceId,
    bool IsVerifiedByHuman,
    Guid? VerifiedBy,
    DateTime? VerifiedAt,
    float? ConfidenceScore,
    DateTime? PublishedAt,
    string[] Tags,
    string Language,
    int ViewCount,
    int ChunkCount,
    DateTime CreatedAt,
    Guid? CreatedBy);

public sealed record CreateKnowledgeItemRequest(
    string Title,
    KnowledgeItemType Type,
    string? Content = null,
    string? Summary = null,
    Guid? SourceDocumentId = null,
    Guid? PackId = null,
    string[]? Tags = null,
    string Language = "fr",
    Guid? WorkspaceId = null);

public sealed record UpdateKnowledgeItemRequest(
    string? Title,
    string? Content,
    string? Summary,
    Guid? PackId,
    string[]? Tags,
    string? Language);

public sealed record ValidateKnowledgeItemRequest(string? Note = null);

// ── KnowledgeChunk ────────────────────────────────────────────────────────────

public sealed record KnowledgeChunkDto(
    Guid Id,
    Guid ItemId,
    int ChunkIndex,
    string Content,
    int TokenCount,
    int? StartPage,
    int? EndPage,
    bool IsEmbedded,
    DateTime? EmbeddedAt);

// ── KnowledgePack ─────────────────────────────────────────────────────────────

public sealed record KnowledgePackDto(
    Guid Id,
    string Name,
    string? Description,
    KnowledgePackStatus Status,
    string[] Tags,
    string Language,
    bool IsPublic,
    int ItemCount,
    DateTime? LastExportedAt,
    Guid OwnerId,
    DateTime CreatedAt);

public sealed record CreatePackRequest(
    string Name,
    string? Description = null,
    string[]? Tags = null,
    string Language = "fr",
    bool IsPublic = false);

public sealed record UpdatePackRequest(
    string? Name,
    string? Description,
    string[]? Tags,
    bool? IsPublic);

// ── Knowledge Graph ───────────────────────────────────────────────────────────

public sealed record GraphEntityDto(
    string Id,
    string Type,
    string Label,
    string? Description,
    Dictionary<string, string>? Properties,
    IReadOnlyList<GraphRelationDto>? Relations);

public sealed record GraphRelationDto(
    string Id,
    string SourceId,
    string TargetId,
    string RelationType,
    float? ConfidenceScore);

public sealed record GraphQueryRequest(string Query, Dictionary<string, object>? Parameters = null);

public sealed record CreateKnowledgePackRequest(
    string Name,
    string? Description = null,
    bool IsPublic = false);

public sealed record AskRequest(
    string Question,
    Guid? PackId = null);

public sealed record AskResponse(
    string Answer,
    IReadOnlyList<SourceRef> Sources,
    int PromptTokens,
    int CompletionTokens);

public sealed record SourceRef(
    Guid Id,
    string Title,
    KnowledgeItemType Type);
