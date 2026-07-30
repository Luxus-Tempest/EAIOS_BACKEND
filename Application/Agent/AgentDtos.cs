using EAIOS.Api.Domain.Agent;

namespace EAIOS.Api.Application.Agent;

public sealed record CreateAgentRequest(
    string          Name,
    AgentType       Type,
    string?         DisplayName   = null,
    string?         Description   = null,
    string?         SystemPrompt  = null,
    string?         LlmModel      = "gpt-4o",
    float?          Temperature   = 0.7f,
    int?            MaxTokens     = 4096,
    AgentVisibility Visibility    = AgentVisibility.Private);

public sealed record UpdateAgentRequest(
    string?          DisplayName  = null,
    string?          Description  = null,
    string?          SystemPrompt = null,
    string?          LlmModel     = null,
    float?           Temperature  = null,
    AgentVisibility? Visibility   = null,
    AgentStatus?     Status       = null,
    string[]?        Tags         = null);

public sealed record ExecuteAgentRequest(
    string  Input,
    Guid?   SessionId   = null,
    string? ContextJson = null);

public sealed record UpsertMemoryRequest(
    AgentMemoryType Type,
    string          Key,
    string          Value,
    float?          ImportanceScore = null,
    DateTimeOffset? ExpiresAt       = null);
