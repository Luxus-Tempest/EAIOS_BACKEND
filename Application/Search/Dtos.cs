using EAIOS.Api.Domain.Search;

namespace EAIOS.Api.Application.Search;

// ── Search Request ────────────────────────────────────────────────────────────

public sealed record SearchRequest(
    string Query,
    SearchType Type = SearchType.Hybrid,
    SearchFilters? Filters = null,
    string[]? Facets = null,
    bool Highlight = true,
    int Page = 1,
    int PageSize = 20);

public sealed record SearchFilters(
    Guid? WorkspaceId = null,
    Guid? FolderId = null,
    string[]? Classification = null,
    string[]? Tags = null,
    string[]? MimeTypes = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? Language = null);

public sealed record SemanticSearchRequest(
    string Query,
    int TopK = 10,
    float MinScore = 0.7f,
    SearchFilters? Filters = null);

// ── Search Response ───────────────────────────────────────────────────────────

public sealed record SearchResponse(
    IReadOnlyList<SearchResultDto> Results,
    Dictionary<string, IReadOnlyList<FacetValue>>? Facets,
    int TotalResults,
    int SearchDurationMs,
    int Page,
    int PageSize);

public sealed record SearchResultDto(
    string Id,
    string Title,
    string? Excerpt,
    float RelevanceScore,
    string? Classification,
    string? MimeType,
    DateTime LastModified,
    string[]? Tags,
    Guid? DocumentId,
    string? HighlightedContent);

public sealed record FacetValue(string Value, int Count);

// ── RAG / Ask ────────────────────────────────────────────────────────────────

public sealed record AskRequest(
    string Question,
    SearchFilters? ContextFilters = null,
    bool IncludeCitations = true,
    bool StreamResponse = false);

public sealed record AskResponse(
    string Answer,
    IReadOnlyList<SearchCitationDto> Citations,
    float Confidence,
    TokenUsageDto Tokens,
    int DurationMs);

public sealed record SearchCitationDto(
    Guid DocumentId,
    string Title,
    int? PageNumber,
    float ConfidenceScore,
    string? Excerpt);

public sealed record TokenUsageDto(int Total, int Prompt, int Completion);

// ── Saved Search ──────────────────────────────────────────────────────────────

public sealed record SavedSearchDto(
    Guid Id,
    string Name,
    string? Description,
    string QueryText,
    SearchType SearchType,
    bool AlertEnabled,
    string? AlertFrequency,
    int UsageCount,
    DateTime? LastExecutedAt,
    bool IsShared,
    DateTime CreatedAt);

public sealed record CreateSavedSearchRequest(
    string Name,
    string QueryText,
    SearchType SearchType = SearchType.Hybrid,
    string? FiltersJson = null,
    bool AlertEnabled = false,
    string? AlertFrequency = null,
    string? Description = null);
