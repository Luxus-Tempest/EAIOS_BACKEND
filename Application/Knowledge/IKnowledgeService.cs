using EAIOS.Api.Domain.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public interface IKnowledgeService
{
    Task<KnowledgeItem> CreateItemAsync(Guid tenantId, CreateKnowledgeItemRequest request, Guid actorId, CancellationToken ct = default);
    Task<KnowledgeItem> UpdateItemAsync(Guid id, UpdateKnowledgeItemRequest request, CancellationToken ct = default);
    Task<KnowledgeItem> PublishItemAsync(Guid id, Guid actorId, CancellationToken ct = default);
    Task<KnowledgeItem> ValidateItemAsync(Guid id, Guid actorId, CancellationToken ct = default);
    Task DeleteItemAsync(Guid id, CancellationToken ct = default);
    
    Task<KnowledgePack> CreatePackAsync(Guid tenantId, CreatePackRequest request, Guid actorId, CancellationToken ct = default);
    Task<KnowledgePack> UpdatePackAsync(Guid id, UpdatePackRequest request, CancellationToken ct = default);
    /// <summary>Publier rend les fiches du pack citables par les agents abonnés ; archiver les en retire.</summary>
    Task<KnowledgePack> PublishPackAsync(Guid id, CancellationToken ct = default);
    Task<KnowledgePack> ArchivePackAsync(Guid id, CancellationToken ct = default);
    Task DeletePackAsync(Guid id, CancellationToken ct = default);

    Task<AskResponse> AskAsync(string question, Guid? packId, CancellationToken ct = default);
}
