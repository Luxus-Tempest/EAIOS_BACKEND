using EAIOS.Api.Domain.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public interface IKnowledgeGraphService
{
    Task<GraphEntityDto> GetEntityAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GraphRelationDto>> GetRelationsAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Parcours en largeur autour d'un noeud, borné en profondeur et en taille.</summary>
    Task<GraphResultDto> GetSubgraphAsync(Guid rootId, int depth, GraphDirection direction,
        string[]? relationTypes = null, int maxNodes = 200, CancellationToken ct = default);

    /// <summary>Plus court chemin entre deux noeuds, ou <c>null</c> si aucun n'existe dans la limite fixée.</summary>
    Task<GraphPathDto?> FindPathAsync(Guid sourceId, Guid targetId, int maxDepth = 5, CancellationToken ct = default);

    Task<KnowledgeRelation> CreateRelationAsync(Guid tenantId, Guid sourceId, Guid targetId,
        string relationType, Guid actorId, string? label = null, float? confidence = null,
        CancellationToken ct = default);

    Task DeleteRelationAsync(Guid relationId, CancellationToken ct = default);

    Task<object> ExecuteGraphQueryAsync(string query, Dictionary<string, object>? parameters, CancellationToken ct = default);
}

public enum GraphDirection { Outgoing, Incoming, Both }

public sealed record GraphNodeDto(
    string Id,
    string Type,
    string Label,
    string? Description,
    string Status,
    int Depth);

public sealed record GraphResultDto(
    string RootId,
    int Depth,
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphRelationDto> Edges,
    bool Truncated);

public sealed record GraphPathDto(
    string SourceId,
    string TargetId,
    int Length,
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphRelationDto> Edges);

public sealed record CreateRelationRequest(
    Guid SourceItemId,
    Guid TargetItemId,
    string RelationType,
    string? Label = null,
    float? ConfidenceScore = null);
