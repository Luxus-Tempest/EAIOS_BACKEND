using EAIOS.Api.Application.Agent;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Agents IA : CRUD, exécution, mémoire, prompts.
/// </summary>
[Route("api/v1/agents")]
public sealed class AgentsController(
    IAgentRepository          agentRepo,
    IAgentExecutionRepository executionRepo,
    IAgentMemoryRepository    memoryRepo,
    ILlmService               llm) : V1ApiController
{
    // ── Agents CRUD ───────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] AgentType? type,
        [FromQuery] AgentStatus? status,
        [FromQuery] AgentVisibility? visibility,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await agentRepo.SearchAsync(q, type, status, visibility, page, pageSize, ct);
        return OkList(result.Items.Select(MapAgent).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("{id:guid}", Name = "GetAgent")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct);
        return agent == null ? NotFound() : Ok200(MapAgent(agent));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAgentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var agent = Domain.Agent.Agent.Create(
            TenantId, req.Name, req.Type, ActorId.Value, req.Description, req.SystemPrompt);

        await agentRepo.AddAsync(agent, ct);
        await agentRepo.SaveAsync(ct);
        return Created201("GetAgent", new { id = agent.Id }, MapAgent(agent));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAgentRequest req, CancellationToken ct)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct);
        if (agent == null) return NotFound();
        var llmConfigJson = req.LlmConfig != null ? System.Text.Json.JsonSerializer.Serialize(req.LlmConfig) : null;
        agent.Update(req.DisplayName, req.Description, req.SystemPrompt, llmConfigJson, req.KnowledgePackIds, req.EnabledTools, req.MemoryEnabled);
        agentRepo.Update(agent);
        await agentRepo.SaveAsync(ct);
        return Ok200(MapAgent(agent));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct);
        if (agent == null) return NotFound();
        agentRepo.SoftDelete(agent);
        await agentRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Exécution ─────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/execute")]
    public async Task<IActionResult> Execute(Guid id, [FromBody] ExecuteAgentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var agent = await agentRepo.GetByIdAsync(id, ct);
        if (agent == null) return NotFound();
        if (agent.Status != AgentStatus.Published && agent.Status != AgentStatus.Draft)
            return UnprocessableEntity("Cet agent n'est pas actif.");

        Guid.TryParse(req.SessionId, out var parsedSessionId);
        var execution = AgentExecution.Create(TenantId, id, "1.0.0", req.Input, ActorId.Value, parsedSessionId == Guid.Empty ? null : parsedSessionId);
        execution.Start();
        await executionRepo.AddAsync(execution, ct);
        await executionRepo.SaveAsync(ct);

        try
        {
            var opts = new LlmOptions("gpt-4o", 0.7f);
            var result = await llm.GenerateAsync(agent.SystemPrompt ?? "", req.Input, opts, ct);

            execution.Complete(result.Output, result.PromptTokens, result.CompletionTokens, result.CostUsd, result.ModelUsed);
        }
        catch (Exception ex)
        {
            execution.Fail("EXECUTION_FAILED", ex.Message);
        }

        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);

        return Ok200(MapExecution(execution));
    }

    [HttpPost("{id:guid}/execute/stream")]
    public async Task ExecuteStream(Guid id, [FromBody] ExecuteAgentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) { Response.StatusCode = 401; return; }

        var agent = await agentRepo.GetByIdAsync(id, ct);
        if (agent == null) { Response.StatusCode = 404; return; }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        await using var writer = new StreamWriter(Response.Body);
        var opts = new LlmOptions("gpt-4o", 0.7f);

        await foreach (var chunk in llm.StreamAsync(agent.SystemPrompt ?? "", req.Input, opts, ct))
        {
            await writer.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(chunk)}\n\n");
            await writer.FlushAsync(ct);
        }

        await writer.WriteAsync("data: [DONE]\n\n");
    }

    // ── Historique d'exécutions ───────────────────────────────────────────────

    [HttpGet("{id:guid}/executions")]
    public async Task<IActionResult> ListExecutions(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await executionRepo.GetByAgentAsync(id, page, pageSize, ct);
        return OkList(result.Items.Select(MapExecution).ToList(), result.TotalCount, page, pageSize);
    }

    // ── Mémoire ───────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/memory")]
    public async Task<IActionResult> GetMemory(Guid id, [FromQuery] AgentMemoryType? type, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var memories = await memoryRepo.GetByAgentAsync(id, ActorId.Value, type, ct);
        return Ok200(memories.Select(m => new { m.Id, m.Key, Value = m.Content, m.Type, m.ImportanceScore, m.AccessCount, m.ExpiresAt, m.CreatedAt }).ToList());
    }

    [HttpPost("{id:guid}/memory")]
    public async Task<IActionResult> UpsertMemory(Guid id, [FromBody] UpsertMemoryRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var existing = await memoryRepo.FindByKeyAsync(id, ActorId.Value, req.Type, req.Key, ct);
        if (existing != null)
        {
            existing.UpdateContent(req.Value, req.ImportanceScore);
            memoryRepo.Update(existing);
        }
        else
        {
            var memory = AgentMemory.Create(TenantId, id, req.Type, req.Key, req.Value, ActorId.Value, req.ImportanceScore);
            await memoryRepo.AddAsync(memory, ct);
        }

        await memoryRepo.SaveAsync(ct);
        return Ok(new { message = "Mémoire mise à jour." });
    }

    [HttpDelete("{id:guid}/memory/{memoryId:guid}")]
    public async Task<IActionResult> DeleteMemory(Guid id, Guid memoryId, CancellationToken ct)
    {
        var memory = await memoryRepo.GetByIdAsync(memoryId, ct);
        if (memory == null || memory.AgentId != id) return NotFound();
        memoryRepo.SoftDelete(memory);
        await memoryRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapAgent(Domain.Agent.Agent a) => new
    {
        a.Id, a.Name, a.DisplayName, a.Description, a.Type, a.Status, a.Visibility,
        a.SystemPrompt, LlmModel = "gpt-4o", Temperature = 0.7f, MaxTokens = 4096, a.Tags, a.CreatedAt, a.UpdatedAt
    };

    private static object MapExecution(AgentExecution e) => new
    {
        e.Id, e.AgentId, e.Status, Input = e.InputText, Output = e.OutputText, e.PromptTokens, e.CompletionTokens,
        e.CostUsd, DurationMs = e.Duration?.TotalMilliseconds, e.ErrorMessage, e.StartedAt, e.CompletedAt
    };
}
