using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public sealed class KnowledgeGraphService(
    IKnowledgeItemRepository itemRepo) : IKnowledgeGraphService
{
    public async Task<GraphEntityDto> GetEntityAsync(Guid id, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Entité introuvable dans le graphe.");

        // On mappe le KnowledgeItem en GraphEntityDto
        return new GraphEntityDto(
            Id: item.Id.ToString(),
            Type: item.Type.ToString(),
            Label: item.Title,
            Description: item.Summary,
            Properties: new Dictionary<string, string>
            {
                { "Status", item.Status.ToString() },
                { "Language", item.Language },
                { "Source", item.Source.ToString() }
            },
            Relations: [] // L'implémentation complète nécessiterait IKnowledgeRelationRepository
        );
    }

    public Task<IReadOnlyList<GraphRelationDto>> GetRelationsAsync(Guid entityId, CancellationToken ct = default)
    {
        // Simulation, à brancher sur un IKnowledgeRelationRepository
        IReadOnlyList<GraphRelationDto> relations = [];
        return Task.FromResult(relations);
    }

    public Task<object> ExecuteGraphQueryAsync(string query, Dictionary<string, object>? parameters, CancellationToken ct = default)
    {
        // Placeholder pour de futures requêtes Gremlin/Cypher
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("La requête ne peut pas être vide.");
            
        return Task.FromResult<object>(new { 
            message = "Exécution de requête Graph non implémentée.",
            query = query,
            parameters = parameters
        });
    }
}
