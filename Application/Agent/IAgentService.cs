using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Application.Agent;

namespace EAIOS.Api.Application.Agent;

public interface IAgentService
{
    Task<Domain.Agent.Agent> CreateAgentAsync(Guid tenantId, string name, AgentType type, Guid actorId, string? description, string? systemPrompt, CancellationToken ct = default);
    /// <summary>Création complète : modèle, packs, outils, étiquettes et espace sont posés dès le départ.</summary>
    Task<Domain.Agent.Agent> CreateAgentAsync(Guid tenantId, CreateAgentRequest request, Guid actorId, CancellationToken ct = default);
    Task<Domain.Agent.Agent> UpdateAgentAsync(Guid id, string? displayName, string? description, string? systemPrompt, AgentLlmConfigDto? llmConfig, Guid[]? knowledgePackIds, string[]? enabledTools, bool? memoryEnabled, CancellationToken ct = default,
        AgentVisibility? visibility = null, string[]? tags = null);
    Task DeleteAgentAsync(Guid id, CancellationToken ct = default);

    Task<AgentExecution> ExecuteAsync(Guid tenantId, Guid agentId, string input, Guid actorId, Guid? sessionId = null, CancellationToken ct = default);

    /// <summary>
    /// Même exécution, diffusée. Les événements sont relayés tels quels ; le
    /// dernier — `done` — porte le résultat complet, qui est consigné dans
    /// l'exécution avant que le flux ne se termine.
    /// </summary>
    IAsyncEnumerable<EAIOS.Api.Infrastructure.AI.RuntimeStreamEvent> ExecuteStreamAsync(
        Guid tenantId, Guid agentId, string input, Guid actorId, Guid? sessionId = null, CancellationToken ct = default);

    // ── Versions figées ───────────────────────────────────────────────────────
    // Une exécution utilise toujours un instantané : la configuration ne bouge
    // pas sous les pieds d'une conversation en cours, et une reprise après arrêt
    // humain retrouve exactement l'agent qui s'était arrêté.

    Task<AgentVersion> PublishAsync(Guid tenantId, Guid agentId, Guid actorId, string? changeLog, CancellationToken ct = default);

    /// <summary>
    /// Résout une référence de version. Accepte un numéro, <c>latest</c> ou
    /// <c>published</c> — le runtime transmet la référence telle qu'elle figure
    /// dans la portée signée, sans l'interpréter.
    /// </summary>
    Task<AgentSnapshotDto> GetVersionSnapshotAsync(Guid agentId, string versionRef, CancellationToken ct = default);

    Task<IReadOnlyList<AgentVersion>> ListVersionsAsync(Guid agentId, CancellationToken ct = default);


    Task UpsertMemoryAsync(Guid tenantId, Guid agentId, AgentMemoryType type, string key, string value, Guid actorId, float importanceScore = 1.0f, CancellationToken ct = default);
    Task DeleteMemoryAsync(Guid agentId, Guid memoryId, CancellationToken ct = default);
}
