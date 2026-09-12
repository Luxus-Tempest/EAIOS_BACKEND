using System.Text.Json;
using System.Text.Json.Serialization;

namespace EAIOS.Api.Application.Workflow;

/// <summary>
/// Types de noeuds reconnus par le moteur. Tout type inconnu est traité comme
/// une étape automatique traversante, pour qu'un graphe enrichi côté éditeur
/// ne bloque jamais une instance en cours.
/// </summary>
public static class WorkflowNodeTypes
{
    public const string Start     = "start";
    public const string End       = "end";
    public const string Approval  = "approval";   // tâche humaine — approuver / rejeter
    public const string Review    = "review";     // tâche humaine — relecture
    public const string DataInput = "datainput";  // tâche humaine — saisie de formulaire
    public const string Condition = "condition";  // branchement sur variable
    public const string Automatic = "automatic";  // étape traversée sans intervention
    public const string Agent     = "agent";      // délégué à un agent IA

    public static bool IsHumanTask(string type) =>
        type is Approval or Review or DataInput;

    public static bool IsTerminal(string type) => type == End;
}

// ── Modèle sérialisé du graphe ────────────────────────────────────────────────

public sealed class WorkflowGraphDocument
{
    [JsonPropertyName("nodes")] public List<WorkflowNode> Nodes { get; set; } = [];
    [JsonPropertyName("edges")] public List<WorkflowEdge> Edges { get; set; } = [];
}

public sealed class WorkflowNode
{
    [JsonPropertyName("id")]           public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")]         public string Type { get; set; } = WorkflowNodeTypes.Automatic;
    [JsonPropertyName("label")]        public string? Label { get; set; }
    [JsonPropertyName("instructions")] public string? Instructions { get; set; }

    /// <summary>Utilisateur assigné pour les tâches humaines.</summary>
    [JsonPropertyName("assigneeId")]   public Guid? AssigneeId { get; set; }
    [JsonPropertyName("assigneeType")] public string? AssigneeType { get; set; }

    /// <summary>Délai en heures avant échéance de la tâche générée.</summary>
    [JsonPropertyName("dueInHours")]   public int? DueInHours { get; set; }

    /// <summary>Variable évaluée par un noeud de type <c>condition</c>.</summary>
    [JsonPropertyName("variable")]     public string? Variable { get; set; }

    /// <summary>Agent délégué par un noeud de type <c>agent</c>. Ses instructions sont le message envoyé.</summary>
    [JsonPropertyName("agentId")]      public Guid? AgentId { get; set; }

    public string NormalizedType => (Type ?? WorkflowNodeTypes.Automatic).Trim().ToLowerInvariant();
    public string DisplayLabel   => string.IsNullOrWhiteSpace(Label) ? Id : Label!;
}

public sealed class WorkflowEdge
{
    [JsonPropertyName("id")]     public string? Id { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Valeur qui doit être produite par le noeud source pour emprunter cette arête
    /// (décision d'une tâche humaine, ou valeur d'une variable pour une condition).
    /// Une arête sans condition est le chemin par défaut.
    /// </summary>
    [JsonPropertyName("condition")] public string? Condition { get; set; }

    public bool IsDefault => string.IsNullOrWhiteSpace(Condition);
}

// ── Graphe exploitable ────────────────────────────────────────────────────────

/// <summary>
/// Vue indexée du graphe d'un workflow, avec résolution du noeud de départ et
/// de la transition suivante. C'est ce qui remplace le « premier noeud » codé
/// en dur et permet au moteur d'avancer réellement d'étape en étape.
/// </summary>
public sealed class WorkflowGraph
{
    private readonly Dictionary<string, WorkflowNode> _nodesById;
    private readonly ILookup<string, WorkflowEdge> _edgesBySource;

    public IReadOnlyList<WorkflowNode> Nodes { get; }
    public IReadOnlyList<WorkflowEdge> Edges { get; }

    private WorkflowGraph(List<WorkflowNode> nodes, List<WorkflowEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
        _nodesById     = nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _edgesBySource = edges.ToLookup(e => e.Source, StringComparer.OrdinalIgnoreCase);
    }

    public static WorkflowGraph Empty { get; } = new([], []);

    /// <summary>Analyse un graphe sérialisé. Renvoie un graphe vide si le JSON est absent ou illisible.</summary>
    public static WorkflowGraph Parse(string? graphJson)
    {
        if (string.IsNullOrWhiteSpace(graphJson)) return Empty;

        try
        {
            var doc = JsonSerializer.Deserialize<WorkflowGraphDocument>(graphJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (doc is null) return Empty;

            var nodes = doc.Nodes.Where(n => !string.IsNullOrWhiteSpace(n.Id)).ToList();
            var edges = doc.Edges
                .Where(e => !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Target))
                .ToList();

            return new WorkflowGraph(nodes, edges);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public bool IsEmpty => Nodes.Count == 0;

    public WorkflowNode? GetNode(string? id) =>
        id is not null && _nodesById.TryGetValue(id, out var node) ? node : null;

    /// <summary>
    /// Noeud d'entrée : le noeud explicitement typé <c>start</c>, à défaut le premier
    /// noeud sans arête entrante, à défaut le premier noeud déclaré.
    /// </summary>
    public WorkflowNode? FindStartNode()
    {
        var explicitStart = Nodes.FirstOrDefault(n => n.NormalizedType == WorkflowNodeTypes.Start);
        if (explicitStart is not null) return explicitStart;

        var targets = Edges.Select(e => e.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphan  = Nodes.FirstOrDefault(n => !targets.Contains(n.Id));

        return orphan ?? Nodes.FirstOrDefault();
    }

    public IEnumerable<WorkflowEdge> OutgoingEdges(string nodeId) => _edgesBySource[nodeId];

    /// <summary>
    /// Résout la transition sortante d'un noeud. <paramref name="decision"/> est la
    /// valeur produite par l'étape (décision d'approbation, valeur de condition) :
    /// une arête dont la condition correspond l'emporte sur l'arête par défaut.
    /// </summary>
    public WorkflowNode? GetNextNode(string nodeId, string? decision)
    {
        var outgoing = _edgesBySource[nodeId].ToList();
        if (outgoing.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(decision))
        {
            var matched = outgoing.FirstOrDefault(e =>
                string.Equals(e.Condition, decision, StringComparison.OrdinalIgnoreCase));
            if (matched is not null) return GetNode(matched.Target);
        }

        var fallback = outgoing.FirstOrDefault(e => e.IsDefault) ?? outgoing[0];
        return GetNode(fallback.Target);
    }

    /// <summary>
    /// Vérifie la cohérence structurelle avant publication. Un workflow publié
    /// avec un graphe cassé produirait des instances bloquées.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (IsEmpty)
        {
            errors.Add("Le graphe ne contient aucun noeud.");
            return errors;
        }

        var duplicates = Nodes.GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1)
                              .Select(g => g.Key)
                              .ToList();
        if (duplicates.Count > 0)
            errors.Add($"Identifiants de noeuds dupliqués : {string.Join(", ", duplicates)}.");

        if (FindStartNode() is null)
            errors.Add("Aucun noeud de départ identifiable.");

        if (!Nodes.Any(n => WorkflowNodeTypes.IsTerminal(n.NormalizedType)))
            errors.Add("Le graphe ne contient aucun noeud de fin (type « end »).");

        foreach (var edge in Edges)
        {
            if (GetNode(edge.Source) is null)
                errors.Add($"L'arête « {edge.Id ?? $"{edge.Source}->{edge.Target}"} » référence un noeud source inconnu : {edge.Source}.");
            if (GetNode(edge.Target) is null)
                errors.Add($"L'arête « {edge.Id ?? $"{edge.Source}->{edge.Target}"} » référence un noeud cible inconnu : {edge.Target}.");
        }

        // Un noeud non terminal sans sortie bloquerait définitivement l'instance.
        foreach (var node in Nodes.Where(n => !WorkflowNodeTypes.IsTerminal(n.NormalizedType)))
        {
            if (!_edgesBySource[node.Id].Any())
                errors.Add($"Le noeud « {node.Id} » n'a aucune transition sortante et n'est pas un noeud de fin.");
        }

        return errors;
    }
}
