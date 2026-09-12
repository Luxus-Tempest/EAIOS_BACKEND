using System.Text.Json.Serialization;

namespace EAIOS.Api.Application.Agent;

// ═══════════════════════════════════════════════════════════════════════════════
// CONTRATS PARTAGÉS AVEC LE RUNTIME D'AGENTS (agent-runtime, Python)
//
// Ces deux formes traversent la frontière .NET → Python. Elles sont figées :
// toute modification doit être répercutée dans `agent-runtime/src/eaios_agents/`
// — `context.py` pour la portée, `runtime/agent.py` pour l'instantané.
//
// Deux conventions de nommage cohabitent, et ce n'est pas une négligence :
//   • l'instantané est en PascalCase, il reflète le domaine .NET ;
//   • la portée est en snake_case, elle est validée par un modèle Pydantic.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Instantané figé d'un agent, sérialisé dans <c>AgentVersion.SnapshotJson</c>.
///
/// Une exécution utilise toujours un instantané : la configuration ne peut pas
/// bouger sous les pieds d'une conversation en cours, et une reprise après arrêt
/// humain retrouve exactement l'agent qui s'était arrêté.
/// </summary>
public sealed record AgentSnapshotDto(
    Guid     AgentId,
    string   Name,
    string?  DisplayName,
    string   Type,
    string?  SystemPrompt,
    string   LlmConfigJson,
    string[] EnabledTools,
    Guid[]   KnowledgePackIds,
    Guid[]   WorkspaceIds,
    bool     RequireHumanConfirmation,
    bool     MemoryEnabled,
    int?     MaxExecutionSeconds,
    int      VersionNumber,
    /// <summary>Sous-agents d'un orchestrateur. Vide pour tout autre type.</summary>
    IReadOnlyList<SubAgentRef>? SubAgents = null);

/// <summary>Reference vers un agent delegue, dans une version figee.</summary>
public sealed record SubAgentRef(Guid AgentId, string Version = "published");

/// <summary>
/// Portée d'exécution signée — un <b>plafond</b> de droits, jamais un plancher.
///
/// Le backend est la seule autorité d'autorisation d'EAIOS. Il décrit ici
/// exactement ce que l'exécution a le droit de voir et de dépenser ; le runtime
/// applique cette portée telle quelle et ne l'élargit jamais.
///
/// Les noms sont en snake_case parce que <c>ExecutionScope</c> (Pydantic) les
/// valide champ par champ côté Python : un renommage ici casse là-bas.
/// </summary>
public sealed record ExecutionScopeDto
{
    [JsonPropertyName("organization_id")] public required Guid OrganizationId { get; init; }

    /// <summary>L'utilisateur réel. Toute écriture métier sera journalisée sous cette identité.</summary>
    [JsonPropertyName("actor_id")] public required Guid ActorId { get; init; }

    [JsonPropertyName("agent_id")] public required Guid AgentId { get; init; }

    /// <summary>Numéro de version figée, en texte — le runtime le renvoie tel quel.</summary>
    [JsonPropertyName("agent_version")] public required string AgentVersion { get; init; }

    /// <summary>Identifie ce tour. Le <c>thread_id</c> LangGraph est le fil (<see cref="SessionId"/>), à défaut ce tour.</summary>
    [JsonPropertyName("execution_id")] public required Guid ExecutionId { get; init; }

    /// <summary>
    /// Le fil de conversation. C'est lui qui devient le <c>thread_id</c> : tous
    /// les tours d'une même conversation partagent la même lignée de checkpoints,
    /// donc la même mémoire. Absent pour un appel isolé (assistant intégré).
    /// </summary>
    [JsonPropertyName("session_id")] public Guid? SessionId { get; init; }

    [JsonPropertyName("knowledge_pack_ids")] public Guid[] KnowledgePackIds { get; init; } = [];
    [JsonPropertyName("workspace_ids")]      public Guid[] WorkspaceIds     { get; init; } = [];

    /// <summary>
    /// Classification maximale consultable, en entier croissant : Public 0,
    /// Internal 1, Confidential 2, StrictlyConfidential 3. L'ordre est celui de
    /// <c>ResourceClassification</c> — la comparaison « au-dessus du plafond »
    /// reste une simple inégalité des deux côtés.
    /// </summary>
    [JsonPropertyName("max_classification")] public required int MaxClassification { get; init; }

    [JsonPropertyName("budget_tokens")] public required int     BudgetTokens { get; init; }
    [JsonPropertyName("budget_usd")]    public required decimal BudgetUsd    { get; init; }

    /// <summary>Échéance absolue. Le runtime refuse de démarrer au-delà.</summary>
    [JsonPropertyName("deadline")] public required DateTimeOffset Deadline { get; init; }

    [JsonPropertyName("allowed_tools")]              public string[] AllowedTools             { get; init; } = [];
    [JsonPropertyName("require_human_confirmation")] public bool     RequireHumanConfirmation { get; init; } = true;
}

/// <summary>
/// Ce que le backend remet à l'appelant du runtime : le jeton de portée signé et
/// l'identifiant d'exécution qu'il désigne.
/// </summary>
public sealed record ExecutionContextDto(
    string           Token,
    Guid             ExecutionId,
    DateTimeOffset   ExpiresAt,
    ExecutionScopeDto Scope);

public sealed record PublishAgentRequest(string? ChangeLog = null);

/// <summary>Ajout d'un cas au jeu de test d'un agent.</summary>
public sealed record CreateTestCaseRequest(
    string Name,
    string Input,
    string[]? ExpectedPhrases = null,
    string[]? ForbiddenPhrases = null,
    bool RequiresCitation = true);
