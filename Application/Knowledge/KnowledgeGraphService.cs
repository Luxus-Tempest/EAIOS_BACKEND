using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;
using System.Text.RegularExpressions;

namespace EAIOS.Api.Application.Knowledge;

/// <summary>
/// Graphe de connaissances construit sur les entités <see cref="KnowledgeItem"/> (noeuds)
/// et <see cref="KnowledgeRelation"/> (arêtes), toutes deux déjà isolées par tenant via
/// les Global Query Filters du DbContext.
///
/// Le langage de requête exposé est un petit DSL de traversée propre au domaine
/// (voir <see cref="ExecuteGraphQueryAsync"/>) plutôt qu'un dialecte Gremlin/Cypher :
/// le stockage est relationnel, et prétendre supporter Cypher exigerait un moteur
/// de graphe dédié.
/// </summary>
public sealed partial class KnowledgeGraphService(
    IKnowledgeItemRepository itemRepo,
    IKnowledgeRelationRepository relationRepo) : IKnowledgeGraphService
{
    private const int MaxDepth = 6;
    private const int HardNodeCap = 1000;

    // ═════════════════════════════════════════════════════════════════════════
    // ENTITÉS & RELATIONS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<GraphEntityDto> GetEntityAsync(Guid id, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Entité introuvable dans le graphe.");

        var relations = await relationRepo.GetByItemAsync(id, includeIncoming: true, ct);

        return new GraphEntityDto(
            Id:          item.Id.ToString(),
            Type:        item.Type.ToString(),
            Label:       item.Title,
            Description: item.Summary,
            Properties: new Dictionary<string, string>
            {
                ["Status"]        = item.Status.ToString(),
                ["Language"]      = item.Language,
                ["Source"]        = item.Source.ToString(),
                ["ViewCount"]     = item.ViewCount.ToString(),
                ["RelationCount"] = relations.Count.ToString(),
                ["CreatedAt"]     = item.CreatedAt.ToString("o")
            },
            Relations: relations.Select(MapRelation).ToList());
    }

    public async Task<IReadOnlyList<GraphRelationDto>> GetRelationsAsync(Guid entityId, CancellationToken ct = default)
    {
        var relations = await relationRepo.GetByItemAsync(entityId, includeIncoming: true, ct);
        return relations.Select(MapRelation).ToList();
    }

    public async Task<KnowledgeRelation> CreateRelationAsync(
        Guid tenantId, Guid sourceId, Guid targetId, string relationType, Guid actorId,
        string? label = null, float? confidence = null, CancellationToken ct = default)
    {
        if (sourceId == targetId)
            throw new ArgumentException("Une entité ne peut pas être reliée à elle-même.");

        if (string.IsNullOrWhiteSpace(relationType))
            throw new ArgumentException("Le type de relation est obligatoire.");

        // Les deux extrémités doivent exister dans le tenant courant.
        _ = await itemRepo.GetByIdAsync(sourceId, ct)
            ?? throw new KeyNotFoundException("Entité source introuvable.");
        _ = await itemRepo.GetByIdAsync(targetId, ct)
            ?? throw new KeyNotFoundException("Entité cible introuvable.");

        var normalizedType = relationType.Trim().ToLowerInvariant();

        if (await relationRepo.ExistsBetweenAsync(sourceId, targetId, normalizedType, ct))
            throw new InvalidOperationException("Cette relation existe déjà entre ces deux entités.");

        var relation = KnowledgeRelation.Create(
            tenantId, sourceId, targetId, normalizedType,
            KnowledgeRelationSource.Manual, actorId);

        await relationRepo.AddAsync(relation, ct);
        await relationRepo.SaveAsync(ct);

        return relation;
    }

    public async Task DeleteRelationAsync(Guid relationId, CancellationToken ct = default)
    {
        var relation = await relationRepo.GetByIdAsync(relationId, ct)
            ?? throw new KeyNotFoundException("Relation introuvable.");

        relationRepo.SoftDelete(relation);
        await relationRepo.SaveAsync(ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TRAVERSÉE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parcours en largeur : chaque niveau charge en une seule requête toutes les arêtes
    /// de la frontière courante, ce qui borne le nombre d'aller-retours base à la profondeur.
    /// </summary>
    public async Task<GraphResultDto> GetSubgraphAsync(
        Guid rootId, int depth, GraphDirection direction,
        string[]? relationTypes = null, int maxNodes = 200, CancellationToken ct = default)
    {
        depth    = Math.Clamp(depth, 1, MaxDepth);
        maxNodes = Math.Clamp(maxNodes, 1, HardNodeCap);

        var root = await itemRepo.GetByIdAsync(rootId, ct)
            ?? throw new KeyNotFoundException("Entité racine introuvable dans le graphe.");

        var typeFilter = relationTypes is { Length: > 0 }
            ? new HashSet<string>(relationTypes.Select(t => t.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase)
            : null;

        var depthById = new Dictionary<Guid, int> { [rootId] = 0 };
        var nodes     = new List<GraphNodeDto> { MapNode(root, 0) };
        var edges     = new Dictionary<Guid, GraphRelationDto>();
        var frontier  = new List<Guid> { rootId };
        var truncated = false;

        for (var level = 1; level <= depth && frontier.Count > 0 && !truncated; level++)
        {
            var relations = await relationRepo.GetByItemsAsync(frontier, ct);
            var nextIds   = new List<Guid>();

            foreach (var rel in relations)
            {
                if (typeFilter is not null && !typeFilter.Contains(rel.RelationType))
                    continue;

                // Ne suivre l'arête que dans le sens demandé, depuis un noeud de la frontière.
                var fromFrontierOutgoing = frontier.Contains(rel.SourceItemId);
                var fromFrontierIncoming = frontier.Contains(rel.TargetItemId);

                var follow = direction switch
                {
                    GraphDirection.Outgoing => fromFrontierOutgoing,
                    GraphDirection.Incoming => fromFrontierIncoming,
                    _                       => fromFrontierOutgoing || fromFrontierIncoming
                };
                if (!follow) continue;

                edges.TryAdd(rel.Id, MapRelation(rel));

                var neighbourId = fromFrontierOutgoing ? rel.TargetItemId : rel.SourceItemId;
                if (depthById.ContainsKey(neighbourId)) continue;

                if (depthById.Count >= maxNodes)
                {
                    truncated = true;
                    break;
                }

                depthById[neighbourId] = level;
                nextIds.Add(neighbourId);
            }

            if (nextIds.Count == 0) break;

            // Hydrate les libellés des noeuds nouvellement atteints.
            foreach (var id in nextIds)
            {
                var item = await itemRepo.GetByIdAsync(id, ct);
                if (item is not null) nodes.Add(MapNode(item, depthById[id]));
            }

            frontier = nextIds;
        }

        // Ne conserver que les arêtes dont les deux extrémités sont dans le résultat,
        // pour que le front n'ait jamais d'arête pendante à dessiner.
        var nodeIds = nodes.Select(n => Guid.Parse(n.Id)).ToHashSet();
        var keptEdges = edges.Values
            .Where(e => nodeIds.Contains(Guid.Parse(e.SourceId)) && nodeIds.Contains(Guid.Parse(e.TargetId)))
            .ToList();

        return new GraphResultDto(rootId.ToString(), depth, nodes, keptEdges, truncated);
    }

    /// <summary>Plus court chemin par BFS depuis la source, en suivant les arêtes dans les deux sens.</summary>
    public async Task<GraphPathDto?> FindPathAsync(Guid sourceId, Guid targetId, int maxDepth = 5, CancellationToken ct = default)
    {
        maxDepth = Math.Clamp(maxDepth, 1, MaxDepth);

        var source = await itemRepo.GetByIdAsync(sourceId, ct)
            ?? throw new KeyNotFoundException("Entité source introuvable.");
        _ = await itemRepo.GetByIdAsync(targetId, ct)
            ?? throw new KeyNotFoundException("Entité cible introuvable.");

        if (sourceId == targetId)
            return new GraphPathDto(sourceId.ToString(), targetId.ToString(), 0, [MapNode(source, 0)], []);

        // predecessor[n] = (noeud précédent, arête empruntée) — permet de remonter le chemin.
        var predecessor = new Dictionary<Guid, (Guid Prev, KnowledgeRelation Edge)>();
        var visited     = new HashSet<Guid> { sourceId };
        var frontier    = new List<Guid> { sourceId };

        for (var level = 1; level <= maxDepth && frontier.Count > 0; level++)
        {
            var relations = await relationRepo.GetByItemsAsync(frontier, ct);
            var nextIds   = new List<Guid>();

            foreach (var rel in relations)
            {
                Guid from, to;
                if (frontier.Contains(rel.SourceItemId))      { from = rel.SourceItemId; to = rel.TargetItemId; }
                else if (frontier.Contains(rel.TargetItemId)) { from = rel.TargetItemId; to = rel.SourceItemId; }
                else continue;

                if (!visited.Add(to)) continue;

                predecessor[to] = (from, rel);

                if (to == targetId)
                    return await BuildPathAsync(sourceId, targetId, predecessor, ct);

                nextIds.Add(to);
            }

            frontier = nextIds;
        }

        return null;   // aucun chemin dans la limite de profondeur
    }

    private async Task<GraphPathDto> BuildPathAsync(
        Guid sourceId, Guid targetId,
        Dictionary<Guid, (Guid Prev, KnowledgeRelation Edge)> predecessor,
        CancellationToken ct)
    {
        var chainIds = new List<Guid>();
        var chainEdges = new List<KnowledgeRelation>();

        var cursor = targetId;
        while (cursor != sourceId)
        {
            chainIds.Add(cursor);
            var (prev, edge) = predecessor[cursor];
            chainEdges.Add(edge);
            cursor = prev;
        }
        chainIds.Add(sourceId);

        chainIds.Reverse();
        chainEdges.Reverse();

        var nodes = new List<GraphNodeDto>();
        for (var i = 0; i < chainIds.Count; i++)
        {
            var item = await itemRepo.GetByIdAsync(chainIds[i], ct);
            if (item is not null) nodes.Add(MapNode(item, i));
        }

        return new GraphPathDto(
            sourceId.ToString(), targetId.ToString(),
            chainEdges.Count, nodes,
            chainEdges.Select(MapRelation).ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DSL DE REQUÊTE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exécute une requête de traversée. Formes supportées :
    /// <list type="bullet">
    ///   <item><c>neighbors(&lt;id&gt;, depth=2, direction=both, types=a|b)</c></item>
    ///   <item><c>subgraph(&lt;id&gt;, depth=3, maxNodes=100)</c> — alias de neighbors</item>
    ///   <item><c>path(&lt;sourceId&gt;, &lt;targetId&gt;, maxDepth=5)</c></item>
    ///   <item><c>relations(&lt;id&gt;)</c></item>
    /// </list>
    /// Les identifiants peuvent être fournis littéralement ou via <paramref name="parameters"/>
    /// en préfixant par <c>$</c> (ex. <c>neighbors($root, depth=2)</c>).
    /// </summary>
    public async Task<object> ExecuteGraphQueryAsync(
        string query, Dictionary<string, object>? parameters, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("La requête ne peut pas être vide.");

        var match = QueryPattern().Match(query.Trim());
        if (!match.Success)
            throw new ArgumentException(
                "Requête non reconnue. Formes supportées : neighbors(id, depth=2), subgraph(id, depth=2), " +
                "path(sourceId, targetId, maxDepth=5), relations(id).");

        var verb = match.Groups["verb"].Value.ToLowerInvariant();
        var args = SplitArguments(match.Groups["args"].Value);

        var positional = args.Where(a => !a.Contains('=')).Select(a => a.Trim()).ToList();
        var named = args.Where(a => a.Contains('='))
            .Select(a => a.Split('=', 2))
            .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        switch (verb)
        {
            case "neighbors":
            case "subgraph":
            {
                var rootId    = ResolveGuid(positional.ElementAtOrDefault(0), parameters, "id");
                var depth     = ResolveInt(named.GetValueOrDefault("depth"), parameters, 2);
                var maxNodes  = ResolveInt(named.GetValueOrDefault("maxnodes"), parameters, 200);
                var direction = ParseDirection(named.GetValueOrDefault("direction"));
                var types     = named.GetValueOrDefault("types")
                    ?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var result = await GetSubgraphAsync(rootId, depth, direction, types, maxNodes, ct);
                return new { Query = verb, Result = result };
            }

            case "path":
            {
                var sourceId = ResolveGuid(positional.ElementAtOrDefault(0), parameters, "sourceId");
                var targetId = ResolveGuid(positional.ElementAtOrDefault(1), parameters, "targetId");
                var maxDepth = ResolveInt(named.GetValueOrDefault("maxdepth"), parameters, 5);

                var path = await FindPathAsync(sourceId, targetId, maxDepth, ct);
                return new
                {
                    Query = verb,
                    Found = path is not null,
                    Result = path
                };
            }

            case "relations":
            {
                var id = ResolveGuid(positional.ElementAtOrDefault(0), parameters, "id");
                var relations = await GetRelationsAsync(id, ct);
                return new { Query = verb, Count = relations.Count, Result = relations };
            }

            default:
                throw new ArgumentException($"Verbe de requête inconnu : « {verb} ».");
        }
    }

    // ── Parsing du DSL ────────────────────────────────────────────────────────

    [GeneratedRegex(@"^(?<verb>[a-zA-Z]+)\s*\(\s*(?<args>[^)]*)\s*\)$", RegexOptions.CultureInvariant)]
    private static partial Regex QueryPattern();

    private static List<string> SplitArguments(string args) =>
        string.IsNullOrWhiteSpace(args)
            ? []
            : args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static Guid ResolveGuid(string? token, Dictionary<string, object>? parameters, string argName)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException($"Argument « {argName} » manquant.");

        token = token.Trim().Trim('\'', '"');

        if (token.StartsWith('$'))
        {
            var key = token[1..];
            if (parameters is null || !parameters.TryGetValue(key, out var value))
                throw new ArgumentException($"Paramètre « {key} » non fourni.");
            token = value?.ToString() ?? "";
        }

        if (!Guid.TryParse(token, out var guid))
            throw new ArgumentException($"Argument « {argName} » : « {token} » n'est pas un identifiant valide.");

        return guid;
    }

    private static int ResolveInt(string? token, Dictionary<string, object>? parameters, int fallback)
    {
        if (string.IsNullOrWhiteSpace(token)) return fallback;

        token = token.Trim().Trim('\'', '"');

        if (token.StartsWith('$'))
        {
            var key = token[1..];
            if (parameters is not null && parameters.TryGetValue(key, out var value))
                token = value?.ToString() ?? "";
        }

        return int.TryParse(token, out var n) ? n : fallback;
    }

    private static GraphDirection ParseDirection(string? token) =>
        (token ?? "both").Trim().ToLowerInvariant() switch
        {
            "out" or "outgoing" => GraphDirection.Outgoing,
            "in"  or "incoming" => GraphDirection.Incoming,
            _                   => GraphDirection.Both
        };

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static GraphRelationDto MapRelation(KnowledgeRelation r) => new(
        Id:              r.Id.ToString(),
        SourceId:        r.SourceItemId.ToString(),
        TargetId:        r.TargetItemId.ToString(),
        RelationType:    r.RelationType,
        ConfidenceScore: r.ConfidenceScore);

    private static GraphNodeDto MapNode(KnowledgeItem item, int depth) => new(
        Id:          item.Id.ToString(),
        Type:        item.Type.ToString(),
        Label:       item.Title,
        Description: item.Summary,
        Status:      item.Status.ToString(),
        Depth:       depth);
}
