using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Agent;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum AgentType { Rag, Conversational, TaskAutomation, DataAnalysis, Custom }
public enum AgentStatus { Draft, Published, Deprecated, Archived }
public enum AgentVisibility { Private, Workspace, Organization, Public }
public enum AgentExecutionStatus { Queued, Running, AwaitingHumanInput, Completed, Failed, Cancelled, TimedOut }
public enum AgentMemoryType { ShortTerm, LongTerm, Observation, WorkingMemory, EpisodicMemory }
public enum PromptRole { System, User, Assistant }
public enum LlmProvider { AzureOpenAi, OpenAi, Anthropic, Mistral, Ollama }

// ═══════════════════════════════════════════════════════════════════════════════
// VALUE OBJECT: AgentLlmConfig
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AgentLlmConfig
{
    public LlmProvider Provider { get; set; } = LlmProvider.OpenAi;
    public string Model { get; set; } = "gpt-4o";
    public float Temperature { get; set; } = 0.7f;
    public int MaxOutputTokens { get; set; } = 4096;
    public bool UseStreaming { get; set; } = false;
    public float? TopP { get; set; }
    public int? ContextWindowTokens { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: Agent
// Table: org_{id}.agent.agents
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class Agent : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? Description { get; private set; }
    public AgentType Type { get; private set; }
    public AgentStatus Status { get; private set; }
    public AgentVisibility Visibility { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? Color { get; private set; }
    public Guid OwnerId { get; private set; }

    // ── LLM Configuration ──────────────────────────────────────────────────────
    public string LlmConfigJson { get; private set; } = "{}";  // Serialized AgentLlmConfig

    // ── System Prompt ──────────────────────────────────────────────────────────
    public string? SystemPrompt { get; private set; }
    public Guid? PromptTemplateId { get; private set; }

    // ── Knowledge ──────────────────────────────────────────────────────────────
    public Guid[] KnowledgePackIds { get; private set; } = [];
    public Guid[] WorkspaceIds { get; private set; } = [];

    // ── Tools ──────────────────────────────────────────────────────────────────
    public string[] EnabledTools { get; private set; } = [];

    // ── Scoping ────────────────────────────────────────────────────────────────
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }

    // ── Behaviour ──────────────────────────────────────────────────────────────
    public bool RequireHumanConfirmation { get; private set; }
    public int? MaxExecutionSeconds { get; private set; }
    public bool MemoryEnabled { get; private set; }
    public int? MaxMemoryItems { get; private set; }
    public string[] Tags { get; private set; } = [];

    // ── Versioning ─────────────────────────────────────────────────────────────
    public int VersionNumber { get; private set; } = 1;
    public Guid? PublishedVersionId { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }

    // ── Stats ──────────────────────────────────────────────────────────────────
    public int ExecutionCount { get; private set; }
    public decimal TotalCostUsd { get; private set; }

    // ── Relations ──────────────────────────────────────────────────────────────
    public IReadOnlyList<AgentExecution> Executions { get; private set; } = [];
    public IReadOnlyList<AgentVersion> Versions { get; private set; } = [];

    public static Agent Create(Guid organizationId, string name, AgentType type, Guid ownerId,
        string? description = null, string? systemPrompt = null)
    {
        var agent = new Agent
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim().ToLowerInvariant(),
            DisplayName = name.Trim(),
            Description = description,
            Type = type,
            Status = AgentStatus.Draft,
            Visibility = AgentVisibility.Organization,
            OwnerId = ownerId,
            SystemPrompt = systemPrompt
        };
        agent.SetOrganizationId(organizationId);
        agent.SetCreated(ownerId);
        return agent;
    }

    public void Publish(Guid publishedBy, Guid versionId)
    {
        Status = AgentStatus.Published;
        PublishedAt = DateTime.UtcNow;
        PublishedBy = publishedBy;
        PublishedVersionId = versionId;
    }

    public void Deprecate() => Status = AgentStatus.Deprecated;

    public void Update(string? displayName, string? description, string? systemPrompt,
        string? llmConfigJson, Guid[]? knowledgePackIds, string[]? enabledTools, bool? memoryEnabled)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName.Trim();
        if (description is not null) Description = description;
        if (systemPrompt is not null) SystemPrompt = systemPrompt;
        if (llmConfigJson is not null) LlmConfigJson = llmConfigJson;
        if (knowledgePackIds is not null) KnowledgePackIds = knowledgePackIds;
        if (enabledTools is not null) EnabledTools = enabledTools;
        if (memoryEnabled.HasValue) MemoryEnabled = memoryEnabled.Value;
        // Editing a published agent reverts to draft
        if (Status == AgentStatus.Published) Status = AgentStatus.Draft;
        VersionNumber++;
    }

    public void RecordExecution(decimal costUsd) { ExecutionCount++; TotalCostUsd += costUsd; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentVersion (Immutable snapshot)
// Table: org_{id}.agent.versions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AgentVersion : TenantEntity
{
    public Guid AgentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string SnapshotJson { get; private set; } = "{}";  // Full agent config at publish time
    public string? ChangeLog { get; private set; }
    public Guid PublishedBy { get; private set; }
    public DateTime PublishedAt { get; private set; }

    public static AgentVersion Create(Guid organizationId, Guid agentId, int versionNumber,
        string snapshotJson, Guid publishedBy, string? changeLog = null)
    {
        var v = new AgentVersion
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            VersionNumber = versionNumber,
            SnapshotJson = snapshotJson,
            ChangeLog = changeLog,
            PublishedBy = publishedBy,
            PublishedAt = DateTime.UtcNow
        };
        v.SetOrganizationId(organizationId);
        v.SetCreated(publishedBy);
        return v;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentExecution
// Table: org_{id}.agent.executions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AgentExecution : TenantEntity
{
    public Guid AgentId { get; private set; }
    public string AgentVersion { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public Guid? SessionId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }

    // ── Status ────────────────────────────────────────────────────────────────
    public AgentExecutionStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public TimeSpan? Duration { get; private set; }

    // ── I/O ───────────────────────────────────────────────────────────────────
    public string? InputText { get; private set; }
    public string? InputDataJson { get; private set; }
    public string? OutputText { get; private set; }
    public string? OutputDataJson { get; private set; }
    public Guid[] SourceDocumentIds { get; private set; } = [];
    public string[]? Citations { get; private set; }

    // ── Metrics ───────────────────────────────────────────────────────────────
    public int PromptTokens { get; private set; }
    public int CompletionTokens { get; private set; }
    public int TotalTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public string? ModelUsed { get; private set; }
    public int? StepCount { get; private set; }

    // ── Error ─────────────────────────────────────────────────────────────────
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool RequiresHumanInput { get; private set; }

    public static AgentExecution Create(Guid organizationId, Guid agentId, string agentVersion,
        string? inputText, Guid? userId = null, Guid? sessionId = null)
    {
        var exec = new AgentExecution
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            AgentVersion = agentVersion,
            UserId = userId,
            SessionId = sessionId,
            InputText = inputText,
            Status = AgentExecutionStatus.Queued,
            StartedAt = DateTime.UtcNow
        };
        exec.SetOrganizationId(organizationId);
        exec.SetCreated(userId);
        return exec;
    }

    public void Start() => Status = AgentExecutionStatus.Running;

    public void Complete(string? output, int promptTokens, int completionTokens, decimal costUsd,
        string? modelUsed, string[]? citations = null, Guid[]? sourceDocIds = null)
    {
        Status = AgentExecutionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        Duration = CompletedAt - StartedAt;
        OutputText = output;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        TotalTokens = promptTokens + completionTokens;
        CostUsd = costUsd;
        ModelUsed = modelUsed;
        Citations = citations;
        SourceDocumentIds = sourceDocIds ?? [];
    }

    public void Fail(string errorCode, string errorMessage) { Status = AgentExecutionStatus.Failed; CompletedAt = DateTime.UtcNow; ErrorCode = errorCode; ErrorMessage = errorMessage; }
    public void Cancel() { Status = AgentExecutionStatus.Cancelled; CompletedAt = DateTime.UtcNow; }
    public void AwaitHumanInput() { Status = AgentExecutionStatus.AwaitingHumanInput; RequiresHumanInput = true; }
    public void ResumeFromHumanInput(string humanResponse) { Status = AgentExecutionStatus.Running; RequiresHumanInput = false; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentMemory
// Table: org_{id}.agent.memories
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AgentMemory : TenantEntity
{
    public Guid AgentId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? ExecutionId { get; private set; }
    public AgentMemoryType Type { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string? MetadataJson { get; private set; }
    public float? ImportanceScore { get; private set; }
    public string? QdrantPointId { get; private set; }
    public DateTime? LastAccessedAt { get; private set; }
    public int AccessCount { get; private set; }
    public DateTime? ExpiresAt { get; private set; }

    public static AgentMemory Create(Guid organizationId, Guid agentId, AgentMemoryType type,
        string key, string content, Guid? userId = null, float? importanceScore = null)
    {
        var m = new AgentMemory
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            UserId = userId,
            Type = type,
            Key = key,
            Content = content,
            ImportanceScore = importanceScore
        };
        m.SetOrganizationId(organizationId);
        m.SetCreated(userId);
        return m;
    }

    public void RecordAccess() { LastAccessedAt = DateTime.UtcNow; AccessCount++; }
    public void UpdateContent(string content, float? importanceScore = null) { Content = content; if (importanceScore.HasValue) ImportanceScore = importanceScore; }
    public void SetQdrantId(string pointId) => QdrantPointId = pointId;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: PromptTemplate
// Table: org_{id}.agent.prompt_templates
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class PromptTemplate : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public PromptRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string[] Variables { get; private set; } = [];
    public string? Language { get; private set; }
    public bool IsSystem { get; private set; }
    public new string Version { get; private set; } = "1.0.0";
    public Guid? ParentTemplateId { get; private set; }

    public static PromptTemplate Create(Guid organizationId, string name, PromptRole role,
        string content, Guid createdBy, string[]? variables = null)
    {
        var pt = new PromptTemplate
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Role = role,
            Content = content,
            Variables = variables ?? []
        };
        pt.SetOrganizationId(organizationId);
        pt.SetCreated(createdBy);
        return pt;
    }
}
