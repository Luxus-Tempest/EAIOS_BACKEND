using EAIOS.Api.Domain.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public interface IKnowledgeGraphService
{
    Task<GraphEntityDto> GetEntityAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GraphRelationDto>> GetRelationsAsync(Guid entityId, CancellationToken ct = default);
    Task<object> ExecuteGraphQueryAsync(string query, Dictionary<string, object>? parameters, CancellationToken ct = default);
}
