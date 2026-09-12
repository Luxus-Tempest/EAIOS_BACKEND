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
    string? Language,
    /// <summary>Vrai pour retirer la fiche de son pack : <c>PackId</c> absent veut dire « inchangé ».</summary>
    bool ClearPack = false);

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

/// <summary>Contrat de lecture d'un pack — le seul, servi par le contrôleur.</summary>
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
    DateTime CreatedAt,
    DateTime? UpdatedAt = null);

public sealed record CreatePackRequest(
    string Name,
    string? Description = null,
    string[]? Tags = null,
    string Language = "fr",
    bool IsPublic = false);

public sealed record UpdatePackRequest(
    string? Name = null,
    string? Description = null,
    string[]? Tags = null,
    bool? IsPublic = null,
    string? Language = null);

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

public sealed record AskRequest(
    string Question,
    Guid? PackId = null);

public sealed record AskResponse(
    string Answer,
    IReadOnlyList<SourceRef> Sources,
    int PromptTokens,
    int CompletionTokens,
    // ── Ajouts : le design exige la citation à la page et à l'article ────────
    // Optionnels, pour que les consommateurs existants du contrat ne cassent pas.
    IReadOnlyList<AnswerCitation>? Citations = null,
    IReadOnlyList<string>? Unresolved = null,
    string RetrievalMode = "agentic");

/// <summary>« [1] Contrat-cadre v12 · p. 3, art. 7.1 », décomposé.</summary>
public sealed record AnswerCitation(
    int Index,
    Guid? DocumentId,
    Guid? KnowledgeItemId,
    string Title,
    int? Page,
    string? Reference);

public sealed record SourceRef(
    Guid Id,
    string Title,
    KnowledgeItemType Type);
