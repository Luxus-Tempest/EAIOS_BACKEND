namespace EAIOS.Api.Application.Search;

public sealed record SearchRequest(
    string?  Query       = null,
    string?  Type        = null,   // "document" | "knowledge" | "agent" | "all"
    Guid?    WorkspaceId = null,
    Guid?    FolderId    = null,
    string?  Scope       = null,
    bool     Semantic    = true,
    int      Page        = 1,
    int      PageSize    = 20);

public sealed record SuggestRequest(string Query, int Limit = 10);

public sealed record AskRequest(string Question, Guid? PackId = null, string Language = "fr");

public sealed record SaveSearchRequest(
    string  Name,
    string? Query    = null,
    string? Filters  = null,
    bool    IsShared = false);

public sealed record SearchResultItem(
    Guid     Id,
    string   Type,
    string   Title,
    string   Snippet,
    DateTimeOffset CreatedAt,
    float    Score);
