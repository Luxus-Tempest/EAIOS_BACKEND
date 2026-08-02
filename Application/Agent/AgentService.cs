using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using System.Text.Json;

namespace EAIOS.Api.Application.Agent;

public sealed class AgentService(
    IAgentRepository agentRepo,
    IAgentExecutionRepository executionRepo,
    IAgentMemoryRepository memoryRepo,
    ILlmService llm) : IAgentService
{
    public async Task<Domain.Agent.Agent> CreateAgentAsync(Guid tenantId, string name, AgentType type, Guid actorId, string? description, string? systemPrompt, CancellationToken ct = default)
    {
        var agent = Domain.Agent.Agent.Create(tenantId, name, type, actorId, description, systemPrompt);
        
        await agentRepo.AddAsync(agent, ct);
        await agentRepo.SaveAsync(ct);
        
        return agent;
    }

    public async Task<Domain.Agent.Agent> UpdateAgentAsync(Guid id, string? displayName, string? description, string? systemPrompt, AgentLlmConfigDto? llmConfig, Guid[]? knowledgePackIds, string[]? enabledTools, bool? memoryEnabled, CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Agent introuvable.");
        
        var llmConfigJson = llmConfig != null ? JsonSerializer.Serialize(llmConfig) : null;
        agent.Update(displayName, description, systemPrompt, llmConfigJson, knowledgePackIds, enabledTools, memoryEnabled);
        
        agentRepo.Update(agent);
        await agentRepo.SaveAsync(ct);
        
        return agent;
    }

    public async Task DeleteAgentAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Agent introuvable.");
        
        agentRepo.SoftDelete(agent);
        await agentRepo.SaveAsync(ct);
    }

    public async Task<AgentExecution> ExecuteAsync(Guid tenantId, Guid agentId, string input, Guid actorId, Guid? sessionId = null, CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(agentId, ct) ?? throw new KeyNotFoundException("Agent introuvable.");
        
        if (agent.Status != AgentStatus.Published && agent.Status != AgentStatus.Draft)
            throw new InvalidOperationException("AGENT_NOT_ACTIVE");

        var execution = AgentExecution.Create(tenantId, agentId, "1.0.0", input, actorId, sessionId);
        execution.Start();
        
        await executionRepo.AddAsync(execution, ct);
        await executionRepo.SaveAsync(ct);

        try
        {
            var opts = new LlmOptions("gpt-4o", 0.7f);
            var result = await llm.GenerateAsync(agent.SystemPrompt ?? "", input, opts, ct);

            execution.Complete(result.Output, result.PromptTokens, result.CompletionTokens, result.CostUsd, result.ModelUsed);
        }
        catch (Exception ex)
        {
            execution.Fail("EXECUTION_FAILED", ex.Message);
        }

        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);

        return execution;
    }

    public async Task UpsertMemoryAsync(Guid tenantId, Guid agentId, AgentMemoryType type, string key, string value, Guid actorId, float importanceScore = 1.0f, CancellationToken ct = default)
    {
        var existing = await memoryRepo.FindByKeyAsync(agentId, actorId, type, key, ct);
        if (existing != null)
        {
            existing.UpdateContent(value, importanceScore);
            memoryRepo.Update(existing);
        }
        else
        {
            var memory = AgentMemory.Create(tenantId, agentId, type, key, value, actorId, importanceScore);
            await memoryRepo.AddAsync(memory, ct);
        }

        await memoryRepo.SaveAsync(ct);
    }

    public async Task DeleteMemoryAsync(Guid agentId, Guid memoryId, CancellationToken ct = default)
    {
        var memory = await memoryRepo.GetByIdAsync(memoryId, ct) ?? throw new KeyNotFoundException("Mémoire introuvable.");
        
        if (memory.AgentId != agentId)
            throw new KeyNotFoundException("Mémoire introuvable pour cet agent.");

        memoryRepo.SoftDelete(memory);
        await memoryRepo.SaveAsync(ct);
    }
}
