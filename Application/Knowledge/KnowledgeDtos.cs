using EAIOS.Api.Domain.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public sealed record CreateKnowledgeItemRequest(
    string              Title,
    string              Content,
    KnowledgeItemType   Type,
    KnowledgeItemSource Source    = KnowledgeItemSource.Manual,
    string?             Language  = "fr",
    Guid?               PackId    = null,
    string[]?           Tags      = null);

public sealed record UpdateKnowledgeItemRequest(
    string?   Title    = null,
    string?   Content  = null,
    string[]? Tags     = null,
    string?   Language = null);

public sealed record ValidateKnowledgeItemRequest(string? Note = null);

public sealed record CreateKnowledgePackRequest(
    string  Name,
    string? Description = null,
    bool    IsPublic    = false);

public sealed record AskRequest(
    string Question,
    Guid?  PackId      = null,
    bool   UseRag      = true,
    string Language    = "fr");

public sealed record AskResponse(
    string             Answer,
    IReadOnlyList<SourceRef> Sources,
    int                PromptTokens,
    int                CompletionTokens);

public sealed record SourceRef(Guid Id, string Title, KnowledgeItemType Type);
