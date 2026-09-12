using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Agent;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

// `Orchestrator` est ajoute en fin d'enumeration : le design le montre
// (« Orchestrateur d'instruction · Running · etape 4/7 ») et le runtime le
// branche sur un superviseur. La valeur est persistee en chaine, donc l'ajout ne
// demande aucune conversion des lignes existantes.
public enum AgentType { Rag, Conversational, TaskAutomation, DataAnalysis, Custom, Orchestrator }
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

    /// <summary>
    /// Sous-agents d'un orchestrateur, en JSON : <c>[{"AgentId":…, "Version":"published"}]</c>.
    ///
    /// <para>
    /// Un orchestrateur delegue a des agents <b>publies</b>, dans une version
    /// figee : son comportement ne doit pas changer parce qu'un collegue a
    /// modifie un agent delegue entre-temps. Le runtime les monte comme outils.
    /// </para>
    /// <para>
    /// Vide pour tout autre type. La delegation est limitee a un niveau : un
    /// orchestrateur qui deleguerait a un orchestrateur ouvrirait une recursion
    /// dont le budget ne serait plus lisible.
    /// </para>
    /// </summary>
    public string? SubAgentsJson { get; private set; }

    // ── Scoping ────────────────────────────────────────────────────────────────
    public Guid? WorkspaceId { get; private set; }
    public Guid? DepartmentId { get; private set; }

    // ── Behaviour ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Un agent exige une décision humaine avant toute action qui touche au réel.
    ///
    /// <para>
    /// La valeur par défaut est <c>true</c>, et c'est délibéré : le défaut d'un
    /// drapeau de sécurité doit fermer, pas ouvrir. Un agent créé sans que
    /// personne n'y pense ne doit pas pouvoir écrire dans EAIOS sans qu'on le lui
    /// ait accordé. Le désactiver reste possible, mais devient un geste explicite.
    /// </para>
    /// </summary>
    public bool RequireHumanConfirmation { get; private set; } = true;
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
    public IReadOnlyList<AgentExecution> Executions { get; private set; } = new List<AgentExecution>();
    public IReadOnlyList<AgentVersion> Versions { get; private set; } = new List<AgentVersion>();

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
            SystemPrompt = systemPrompt,
            // Répété ici, pas seulement en initialiseur de propriété : la
            // garantie doit être lisible à l'endroit où l'agent naît.
            RequireHumanConfirmation = true
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

    public void SetSubAgents(string? subAgentsJson)
    {
        SubAgentsJson = subAgentsJson;
        if (Status == AgentStatus.Published) Status = AgentStatus.Draft;
        VersionNumber++;
    }

    /// <summary>
    /// Configuration initiale, à la création : mêmes champs que <see cref="Update"/>
    /// mais sans incrémenter le numéro de version — l'agent n'a encore jamais existé.
    /// </summary>
    public void Configure(string? llmConfigJson, Guid[]? knowledgePackIds, string[]? enabledTools,
        bool memoryEnabled, string[]? tags, Guid? workspaceId)
    {
        if (llmConfigJson is not null) LlmConfigJson = llmConfigJson;
        if (knowledgePackIds is not null) KnowledgePackIds = knowledgePackIds;
        if (enabledTools is not null) EnabledTools = enabledTools;
        MemoryEnabled = memoryEnabled;
        if (tags is not null) Tags = tags;
        if (workspaceId.HasValue) { WorkspaceId = workspaceId; Visibility = AgentVisibility.Workspace; }
    }

    public void Update(string? displayName, string? description, string? systemPrompt,
        string? llmConfigJson, Guid[]? knowledgePackIds, string[]? enabledTools, bool? memoryEnabled,
        AgentVisibility? visibility = null, string[]? tags = null)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName.Trim();
        if (description is not null) Description = description;
        if (systemPrompt is not null) SystemPrompt = systemPrompt;
        if (llmConfigJson is not null) LlmConfigJson = llmConfigJson;
        if (knowledgePackIds is not null) KnowledgePackIds = knowledgePackIds;
        if (enabledTools is not null) EnabledTools = enabledTools;
        if (memoryEnabled.HasValue) MemoryEnabled = memoryEnabled.Value;
        // Le contrat de mise à jour les acceptait ; le service les perdait en silence.
        if (visibility.HasValue) Visibility = visibility.Value;
        if (tags is not null) Tags = tags;
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
        string? modelUsed, string[]? citations = null, Guid[]? sourceDocIds = null,
        int? stepCount = null, string? outputDataJson = null)
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
        // Alimentent la console de supervision : « étape 4/7 » et les renvois
        // numérotés de la conversation. Les champs existaient depuis le début et
        // n'étaient jamais remplis.
        StepCount = stepCount;
        OutputDataJson = outputDataJson;
    }

    /// <summary>Consommation constatée, même lorsque l'exécution n'aboutit pas.</summary>
    public void RecordUsage(int totalTokens, decimal costUsd, string? modelUsed, int? stepCount)
    {
        TotalTokens = totalTokens;
        CostUsd = costUsd;
        if (modelUsed is not null) ModelUsed = modelUsed;
        if (stepCount.HasValue) StepCount = stepCount;
    }

    public void Fail(string errorCode, string errorMessage) { Status = AgentExecutionStatus.Failed; CompletedAt = DateTime.UtcNow; ErrorCode = errorCode; ErrorMessage = errorMessage; }
    public void Cancel() { Status = AgentExecutionStatus.Cancelled; CompletedAt = DateTime.UtcNow; }
    /// <summary>Restée « en cours » au-delà du raisonnable : le planificateur la clôt.</summary>
    public void TimeOut() { Status = AgentExecutionStatus.TimedOut; CompletedAt = DateTime.UtcNow; Duration = CompletedAt - StartedAt; ErrorCode ??= "TIMED_OUT"; ErrorMessage ??= "Délai d'exécution dépassé."; }
    /// <summary>Exécution lancée par un nœud « agent » d'un workflow : l'instance reprend quand elle aboutit.</summary>
    public void LinkToWorkflowInstance(Guid instanceId) => WorkflowInstanceId = instanceId;
    /// <summary>
    /// L'agent s'est arrêté et demande une décision.
    ///
    /// <para>
    /// Ce n'est pas un échec : côté runtime, l'état est checkpointé et
    /// l'exécution reprendra exactement au point d'arrêt. <paramref name="decisionJson"/>
    /// porte la question, les options et les preuves — de quoi construire la
    /// tâche humaine sans rien deviner.
    /// </para>
    /// </summary>
    public void AwaitHumanInput(string? decisionJson = null)
    {
        Status = AgentExecutionStatus.AwaitingHumanInput;
        RequiresHumanInput = true;
        if (decisionJson is not null) OutputDataJson = decisionJson;
    }
    public void ResumeFromHumanInput(string humanResponse) { Status = AgentExecutionStatus.Running; RequiresHumanInput = false; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentConversation
// Table: agent.conversations
//
// Un fil de conversation entre une personne et un agent. Chaque tour est une
// AgentExecution dont SessionId vaut l'identifiant du fil ; côté runtime, le fil
// est le `thread_id` LangGraph, donc la mémoire de la conversation. C'est ce qui
// permet de rouvrir un échange d'hier et de le poursuivre.
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AgentConversation : TenantEntity
{
    public Guid AgentId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public bool IsPinned { get; private set; }
    public int TurnCount { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public AgentExecutionStatus LastStatus { get; private set; }
    public Guid? LastExecutionId { get; private set; }

    /// <summary>Longueur au-delà de laquelle le premier message est coupé pour servir de titre.</summary>
    public const int MaxTitleLength = 80;

    public static AgentConversation Create(Guid organizationId, Guid agentId, Guid userId, string firstInput)
    {
        var conversation = new AgentConversation
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            UserId = userId,
            Title = TitleFrom(firstInput),
            LastActivityAt = DateTime.UtcNow,
            LastStatus = AgentExecutionStatus.Queued
        };
        conversation.SetOrganizationId(organizationId);
        conversation.SetCreated(userId);
        return conversation;
    }

    /// <summary>Le titre par défaut est le début du premier message, sur une ligne.</summary>
    public static string TitleFrom(string input)
    {
        var line = string.Join(' ', input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (line.Length == 0) return "Conversation";
        return line.Length <= MaxTitleLength ? line : line[..(MaxTitleLength - 1)].TrimEnd() + "…";
    }

    public void RecordTurn(AgentExecution execution)
    {
        // Un tour est compté à son premier enregistrement, pas à chaque mise à
        // jour de statut de la même exécution.
        if (LastExecutionId != execution.Id) TurnCount++;
        LastExecutionId = execution.Id;
        LastStatus = execution.Status;
        LastActivityAt = DateTime.UtcNow;
    }

    public void Rename(string title)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = TitleFrom(title);
    }

    public void Pin(bool pinned) => IsPinned = pinned;
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
