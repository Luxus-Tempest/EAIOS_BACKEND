using EAIOS.Api.Domain.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public interface IKnowledgeService
{
    Task<KnowledgeItem> CreateItemAsync(Guid tenantId, string title, KnowledgeItemType type, string? content, Guid? sourceDocumentId, Guid actorId, CancellationToken ct = default);
    Task<KnowledgeItem> UpdateItemAsync(Guid id, string? title, string? content, string? summary, string[]? tags, string? language, CancellationToken ct = default);
    Task<KnowledgeItem> PublishItemAsync(Guid id, Guid actorId, CancellationToken ct = default);
    Task<KnowledgeItem> ValidateItemAsync(Guid id, Guid actorId, CancellationToken ct = default);
    Task DeleteItemAsync(Guid id, CancellationToken ct = default);
    
    Task<KnowledgePack> CreatePackAsync(Guid tenantId, string name, string? description, bool isPublic, Guid actorId, CancellationToken ct = default);
    Task DeletePackAsync(Guid id, CancellationToken ct = default);

    Task<AskResponse> AskAsync(string question, Guid? packId, CancellationToken ct = default);
}
