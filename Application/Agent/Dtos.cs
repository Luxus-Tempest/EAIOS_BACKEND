using EAIOS.Api.Domain.Agent;

namespace EAIOS.Api.Application.Agent;

// ── Agent ────────────────────────────────────────────────────────────────────

public sealed record AgentDto(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    AgentType Type,
    AgentStatus Status,
    AgentVisibility Visibility,
    string? AvatarUrl,
    string? Color,
    Guid OwnerId,
    string? SystemPrompt,
    Guid[] KnowledgePackIds,
    string[] EnabledTools,
    Guid? WorkspaceId,
    bool RequireHumanConfirmation,
    bool MemoryEnabled,
    string[] Tags,
    int VersionNumber,
    DateTime? PublishedAt,
    int ExecutionCount,
    decimal TotalCostUsd,
    DateTime CreatedAt);

/// <summary>
/// Configuration du modèle. <c>Provider</c> est optionnel : absent, le runtime
/// prend le fournisseur par défaut de l'installation — un entier par défaut
/// (<c>0</c> = AzureOpenAi) désignait silencieusement un fournisseur sans clé.
/// </summary>
public sealed record AgentLlmConfigDto(
    string Model,
    float Temperature = 0.7f,
    int MaxOutputTokens = 4096,
    bool UseStreaming = true,
    float? TopP = null,
    LlmProvider? Provider = null);

public sealed record CreateAgentRequest(
    string Name,
    AgentType Type,
    string? Description = null,
    string? SystemPrompt = null,
    AgentLlmConfigDto? LlmConfig = null,
    Guid[]? KnowledgePackIds = null,
    string[]? EnabledTools = null,
    string[]? Tags = null,
    bool MemoryEnabled = false,
    Guid? WorkspaceId = null);

public sealed record UpdateAgentRequest(
    string? DisplayName,
    string? Description,
    string? SystemPrompt,
    AgentLlmConfigDto? LlmConfig,
    Guid[]? KnowledgePackIds,
    string[]? EnabledTools,
    bool? MemoryEnabled,
    AgentVisibility? Visibility,
    string[]? Tags);

public sealed record CloneAgentRequest(string Name, string? DisplayName = null);

// ── Execution ─────────────────────────────────────────────────────────────────

public sealed record ExecuteAgentRequest(
    string Input,
    string? SessionId = null,
    bool Async = false,
    Dictionary<string, object>? InputData = null);

public sealed record AgentExecutionDto(
    Guid Id,
    Guid AgentId,
    string AgentVersion,
    Guid? UserId,
    AgentExecutionStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    long? DurationMs,
    string? InputText,
    string? OutputText,
    string[]? Citations,
    ExecutionMetricsDto? Metrics,
    string? ErrorCode,
    string? ErrorMessage,
    bool RequiresHumanInput);

public sealed record ExecutionMetricsDto(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal CostUsd,
    string? ModelUsed,
    long DurationMs);

public sealed record CitationDto(
    Guid DocumentId,
    string Title,
    int? PageNumber,
    float ConfidenceScore,
    string? Excerpt);

public sealed record HumanInputRequest(
    string Response,
    Dictionary<string, object>? Data = null,
    string? Comment = null);

// ── Agent Version ─────────────────────────────────────────────────────────────

public sealed record AgentVersionDto(
    Guid Id,
    int VersionNumber,
    string? ChangeLog,
    Guid PublishedBy,
    DateTime PublishedAt);

// ── Memory ────────────────────────────────────────────────────────────────────

public sealed record AgentMemoryDto(
    Guid Id,
    AgentMemoryType Type,
    string Key,
    string Content,
    float? ImportanceScore,
    DateTime? LastAccessedAt,
    int AccessCount,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

public sealed record UpsertMemoryRequest(
    EAIOS.Api.Domain.Agent.AgentMemoryType Type,
    string Key,
    string Value,
    float ImportanceScore = 1.0f,
    DateTime? ExpiresAt = null);
