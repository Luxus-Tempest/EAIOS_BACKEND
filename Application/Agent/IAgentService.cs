using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Application.Agent;

namespace EAIOS.Api.Application.Agent;

public interface IAgentService
{
    Task<Domain.Agent.Agent> CreateAgentAsync(Guid tenantId, string name, AgentType type, Guid actorId, string? description, string? systemPrompt, CancellationToken ct = default);
    Task<Domain.Agent.Agent> UpdateAgentAsync(Guid id, string? displayName, string? description, string? systemPrompt, AgentLlmConfigDto? llmConfig, Guid[]? knowledgePackIds, string[]? enabledTools, bool? memoryEnabled, CancellationToken ct = default);
    Task DeleteAgentAsync(Guid id, CancellationToken ct = default);

    Task<AgentExecution> ExecuteAsync(Guid tenantId, Guid agentId, string input, Guid actorId, Guid? sessionId = null, CancellationToken ct = default);
    
    Task UpsertMemoryAsync(Guid tenantId, Guid agentId, AgentMemoryType type, string key, string value, Guid actorId, float importanceScore = 1.0f, CancellationToken ct = default);
    Task DeleteMemoryAsync(Guid agentId, Guid memoryId, CancellationToken ct = default);
}
