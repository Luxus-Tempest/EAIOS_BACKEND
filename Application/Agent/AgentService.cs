using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Security;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EAIOS.Api.Application.Agent;

public sealed class AgentService(
    IAgentRepository agentRepo,
    IAgentExecutionRepository executionRepo,
    IAgentMemoryRepository memoryRepo,
    IAgentVersionRepository versionRepo,
    IAgentContextService contextService,
    IAgentRuntimeClient runtime,
    IAgentDecisionService decisions,
    IHttpContextAccessor httpContextAccessor,
    IAgentConversationRepository conversations,
    EAIOS.Api.Application.Realtime.IRealtimeEventService? realtime = null) : IAgentService
{
    // Les clés de l'instantané sont lues telles quelles par le runtime Python :
    // pas de camelCase, pas d'indentation superflue. Voir AgentRuntimeContracts.
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented        = false,
    };


    public async Task<Domain.Agent.Agent> CreateAgentAsync(Guid tenantId, string name, AgentType type, Guid actorId, string? description, string? systemPrompt, CancellationToken ct = default)
    {
        var agent = Domain.Agent.Agent.Create(tenantId, name, type, actorId, description, systemPrompt);
        
        await agentRepo.AddAsync(agent, ct);
        await agentRepo.SaveAsync(ct);
        
        return agent;
    }

    /// <summary>
    /// `LlmConfigJson` est lu tel quel par le runtime (<c>Provider</c>, <c>Model</c>…).
    /// Le fournisseur y va <b>par son nom</b> : sérialisé en entier, <c>0</c>
    /// (AzureOpenAi) se confondait avec « absent » et le modèle configuré n'était
    /// jamais celui qui tournait.
    /// </summary>
    private static readonly JsonSerializerOptions LlmConfigJsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async Task<Domain.Agent.Agent> CreateAgentAsync(Guid tenantId, CreateAgentRequest request, Guid actorId, CancellationToken ct = default)
    {
        var agent = Domain.Agent.Agent.Create(tenantId, request.Name, request.Type, actorId, request.Description, request.SystemPrompt);

        // Le contrat de création portait déjà tout cela ; seul le nom passait.
        var llmConfigJson = request.LlmConfig != null ? JsonSerializer.Serialize(request.LlmConfig, LlmConfigJsonOptions) : null;
        agent.Configure(llmConfigJson, request.KnowledgePackIds, request.EnabledTools, request.MemoryEnabled, request.Tags, request.WorkspaceId);

        await agentRepo.AddAsync(agent, ct);
        await agentRepo.SaveAsync(ct);

        return agent;
    }

    public async Task<Domain.Agent.Agent> UpdateAgentAsync(Guid id, string? displayName, string? description, string? systemPrompt, AgentLlmConfigDto? llmConfig, Guid[]? knowledgePackIds, string[]? enabledTools, bool? memoryEnabled, CancellationToken ct = default,
        AgentVisibility? visibility = null, string[]? tags = null)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Agent introuvable.");

        var llmConfigJson = llmConfig != null ? JsonSerializer.Serialize(llmConfig, LlmConfigJsonOptions) : null;
        agent.Update(displayName, description, systemPrompt, llmConfigJson, knowledgePackIds, enabledTools, memoryEnabled, visibility, tags);
        
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

    /// <summary>
    /// Exécute un agent en déléguant le raisonnement au runtime.
    ///
    /// <para>
    /// Le backend ne raisonne pas : il fige le périmètre, appelle, et consigne.
    /// L'<c>AgentExecution</c> qui en résulte est un <b>miroir</b> de supervision —
    /// la source de l'état conversationnel reste le checkpointer du runtime.
    /// </para>
    /// </summary>
    public async Task<AgentExecution> ExecuteAsync(Guid tenantId, Guid agentId, string input, Guid actorId, Guid? sessionId = null, CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(agentId, ct) ?? throw new KeyNotFoundException("Agent introuvable.");

        if (agent.Status is AgentStatus.Deprecated or AgentStatus.Archived)
            throw new InvalidOperationException("AGENT_NOT_ACTIVE");

        // Une exécution s'appuie toujours sur une version figée : sans elle, la
        // configuration pourrait changer sous les pieds d'une conversation en
        // cours, et une reprise après arrêt humain ne retrouverait pas l'agent
        // qui s'était arrêté.
        var versionRef = await ResolveExecutableVersionAsync(agentId, agent, ct);

        // Chaque exécution est un tour d'un fil. Sans fil demandé, on en ouvre
        // un : c'est lui que la personne retrouvera dans son historique et
        // pourra poursuivre.
        var conversation = await ResolveConversationAsync(tenantId, agentId, actorId, sessionId, input, ct);
        await AbandonPendingDecisionAsync(conversation, actorId, ct);

        var execution = AgentExecution.Create(tenantId, agentId, versionRef, input, actorId, conversation.Id);
        execution.Start();

        await executionRepo.AddAsync(execution, ct);
        await executionRepo.SaveAsync(ct);

        RuntimeDecisionRequest? pendingDecision = null;

        try
        {
            var context = contextService.Issue(tenantId, actorId, agent, versionRef, execution.Id, conversation.Id);
            var result  = await runtime.RunAsync(context.Token, CallerToken(), input, ct);

            ExecutionMirror.Apply(execution, result);
            pendingDecision = result.IsInterrupted ? result.Interrupt : null;
        }
        catch (AgentRuntimeUnavailableException ex)
        {
            execution.Fail("RUNTIME_UNAVAILABLE", ex.Message);
        }
        catch (Exception ex)
        {
            execution.Fail("EXECUTION_FAILED", ex.Message);
        }

        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);
        await RecordTurnAsync(conversation, execution, ct);

        // Un arrêt n'a de sens que s'il atteint quelqu'un : sans tâche ni
        // notification, l'exécution attendrait une décision que personne ne sait
        // devoir prendre.
        if (execution.Status == AgentExecutionStatus.AwaitingHumanInput && pendingDecision is not null)
            await decisions.OpenAsync(execution, pendingDecision, ct);

        if (execution.Status == AgentExecutionStatus.Completed)
        {
            agent.RecordExecution(execution.CostUsd);
            agentRepo.Update(agent);
            await agentRepo.SaveAsync(ct);
        }

        return execution;
    }

    /// <summary>
    /// Le fil demandé, ou un fil neuf. Un fil n'appartient qu'à la personne qui
    /// l'a ouvert, pour un seul agent : reprendre le fil d'un autre, ou avec un
    /// autre agent, est refusé comme s'il n'existait pas.
    /// </summary>
    /// <summary>
    /// Un nouveau message alors qu'une décision est en attente vaut refus : le
    /// runtime clôt l'arrêt de la même façon (« reject ») avant de traiter le
    /// message. Le miroir suit — l'exécution est annulée et sa tâche close —
    /// sinon le bandeau et la file montreraient une décision que plus personne
    /// n'attend.
    /// </summary>
    private async Task AbandonPendingDecisionAsync(AgentConversation conversation, Guid actorId, CancellationToken ct)
    {
        var turns = await executionRepo.GetBySessionAsync(conversation.Id, ct);
        var pending = turns.Where(t => t.Status == AgentExecutionStatus.AwaitingHumanInput).ToList();
        if (pending.Count == 0) return;

        foreach (var turn in pending)
        {
            turn.Cancel();
            executionRepo.Update(turn);
        }
        await executionRepo.SaveAsync(ct);

        foreach (var turn in pending)
            await decisions.CloseAsync(turn, actorId, "abandoned",
                "Nouvelle demande de l'utilisateur : l'action en attente est abandonnée.", ct);
    }

    private async Task<AgentConversation> ResolveConversationAsync(
        Guid tenantId, Guid agentId, Guid actorId, Guid? sessionId, string input, CancellationToken ct)
    {
        if (sessionId is { } id)
        {
            var existing = await conversations.GetByIdAsync(id, ct);
            if (existing is null || existing.UserId != actorId || existing.AgentId != agentId)
                throw new KeyNotFoundException("Conversation introuvable.");
            return existing;
        }

        var conversation = AgentConversation.Create(tenantId, agentId, actorId, input);
        await conversations.AddAsync(conversation, ct);
        await conversations.SaveAsync(ct);
        return conversation;
    }

    private async Task RecordTurnAsync(AgentConversation conversation, AgentExecution execution, CancellationToken ct)
    {
        conversation.RecordTurn(execution);
        conversations.Update(conversation);
        await conversations.SaveAsync(ct);

        // La liste des exécutions et l'historique se rafraîchissent en direct.
        if (realtime is not null && execution.UserId is { } user)
            await realtime.PublishToUserAsync(execution.OrganizationId, user, "agent.completed",
                new { executionId = execution.Id, status = execution.Status.ToString(), sessionId = conversation.Id });
    }

    /// <summary>
    /// Exécution diffusée.
    ///
    /// <para>
    /// Le cycle de vie est identique au mode requête/réponse — même version
    /// figée, même portée signée, même miroir — seule la forme du transport
    /// change. Le backend ne réinterprète pas les événements : il les relaie, et
    /// n'ouvre que le `done` final pour consigner l'exécution.
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<RuntimeStreamEvent> ExecuteStreamAsync(
        Guid tenantId, Guid agentId, string input, Guid actorId, Guid? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(agentId, ct) ?? throw new KeyNotFoundException("Agent introuvable.");

        if (agent.Status is AgentStatus.Deprecated or AgentStatus.Archived)
            throw new InvalidOperationException("AGENT_NOT_ACTIVE");

        var versionRef   = await ResolveExecutableVersionAsync(agentId, agent, ct);
        var conversation = await ResolveConversationAsync(tenantId, agentId, actorId, sessionId, input, ct);
        await AbandonPendingDecisionAsync(conversation, actorId, ct);
        var execution    = AgentExecution.Create(tenantId, agentId, versionRef, input, actorId, conversation.Id);
        execution.Start();

        await executionRepo.AddAsync(execution, ct);
        await executionRepo.SaveAsync(ct);

        // Premier événement du flux : les identifiants du tour et du fil. Sans
        // lui, le client ne saurait ni poursuivre la conversation, ni répondre
        // à une décision demandée pendant ce tour.
        yield return new RuntimeStreamEvent("session", JsonSerializer.Serialize(new
        {
            session_id   = conversation.Id,
            execution_id = execution.Id,
        }));

        var context = contextService.Issue(tenantId, actorId, agent, versionRef, execution.Id, conversation.Id);

        RuntimeRunResult? final = null;
        (string Code, string Message)? failure = null;

        await foreach (var evt in runtime.StreamAsync(context.Token, CallerToken(), input, ct))
        {
            if (evt.Event == "done")
                final = ReadFinalResult(evt.Data);
            // Un échec survenu *pendant* la diffusion arrive par le flux : les
            // en-têtes sont déjà partis, un code HTTP n'est plus possible. Il
            // doit malgré tout être consigné, sinon l'exécution resterait
            // « annulée » alors qu'elle a échoué pour une raison connue.
            else if (evt.Event == "error")
                failure = ReadFailure(evt.Data);

            yield return evt;
        }

        if (failure is { } reason)
        {
            execution.Fail(reason.Code, reason.Message);
            executionRepo.Update(execution);
            await executionRepo.SaveAsync(ct);
            await RecordTurnAsync(conversation, execution, ct);
            yield break;
        }

        // Une déconnexion du client interrompt le relais sans `done` ni `error` :
        // l'état reste checkpointé côté runtime, donc reprenable, mais
        // l'exécution ne peut pas être déclarée terminée. Elle est marquée
        // annulée plutôt que laissée éternellement en cours.
        if (final is null)
        {
            execution.Cancel();
            executionRepo.Update(execution);
            await executionRepo.SaveAsync(ct);
            await RecordTurnAsync(conversation, execution, ct);
            yield break;
        }

        ExecutionMirror.Apply(execution, final);
        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);
        await RecordTurnAsync(conversation, execution, ct);

        if (execution.Status == AgentExecutionStatus.AwaitingHumanInput && final.Interrupt is not null)
            await decisions.OpenAsync(execution, final.Interrupt, ct);

        if (execution.Status == AgentExecutionStatus.Completed)
        {
            agent.RecordExecution(execution.CostUsd);
            agentRepo.Update(agent);
            await agentRepo.SaveAsync(ct);
        }

        // Le tour est un fait serveur : même si la fenêtre qui l'a lancé a été
        // fermée, celle qui rouvre le fil apprend qu'il s'est terminé.
        if (realtime is not null)
            await realtime.PublishToUserAsync(tenantId, actorId, "agent.completed",
                new { executionId = execution.Id, status = execution.Status.ToString(), sessionId = conversation.Id });
    }

    /// <summary>Code et message d'un événement `error` du flux.</summary>
    private static (string Code, string Message) ReadFailure(string data)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(data);
            var inner = payload.TryGetProperty("data", out var nested) ? nested : payload;

            return (
                inner.TryGetProperty("errorCode", out var code) ? code.GetString() ?? "EXECUTION_FAILED" : "EXECUTION_FAILED",
                inner.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "");
        }
        catch (JsonException)
        {
            return ("EXECUTION_FAILED", "Échec du flux d'exécution.");
        }
    }

    /// <summary>
    /// Le `done` du runtime est un <c>StreamEvent</c> complet :
    /// <c>{ "type": "done", "data": { "result": … } }</c>. Le résultat vit sous
    /// <c>data.result</c> ; la forme plate <c>{ "result": … }</c> reste acceptée.
    /// Ne pas le trouver, c'était enregistrer « annulée » une exécution qui
    /// venait d'aboutir sous les yeux de l'utilisateur.
    /// </summary>
    private static RuntimeRunResult? ReadFinalResult(string data)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<JsonElement>(data);
            var inner = envelope.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested : envelope;
            return inner.TryGetProperty("result", out var result)
                ? result.Deserialize<RuntimeRunResult>(RuntimeJsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Le runtime sérialise en snake_case (<c>total_tokens</c>, <c>model_used</c>,
    /// <c>document_id</c>). L'insensibilité à la casse ne suffit pas : sans la
    /// politique de nommage, jetons, modèle et identifiants de documents cités
    /// restaient à zéro sur la fiche d'exécution.
    /// </summary>
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Version sur laquelle l'exécution s'appuie.
    ///
    /// <para>
    /// Un agent publié s'exécute sur sa version publiée. Un brouillon jamais
    /// publié s'exécute sur <c>draft</c> — l'instantané est alors construit à la
    /// volée depuis l'état courant, sans être figé en base. C'est le « banc
    /// d'essai » du Studio, qui montre explicitement « brouillon v2.2.0 » : on
    /// doit pouvoir essayer un agent avant de le publier.
    /// </para>
    /// <para>
    /// L'exécution enregistre <c>draft</c> comme version : personne ne confond
    /// un essai avec une exécution reproductible. En contrepartie, une reprise
    /// après arrêt humain relira l'agent tel qu'il est <b>à ce moment-là</b> —
    /// c'est le prix d'un essai sur brouillon, et c'est pourquoi publier reste
    /// le chemin normal.
    /// </para>
    /// </summary>
    private async Task<string> ResolveExecutableVersionAsync(Guid agentId, Domain.Agent.Agent agent, CancellationToken ct)
    {
        if (agent.PublishedVersionId.HasValue) return "published";

        var latest = await versionRepo.FindLatestAsync(agentId, ct);
        if (latest is not null) return latest.VersionNumber.ToString();

        return DraftVersionRef;
    }

    /// <summary>Référence désignant l'état courant, non figé, d'un brouillon.</summary>
    public const string DraftVersionRef = "draft";

    /// <summary>
    /// Jeton d'appel de l'utilisateur réel, relayé tel quel au runtime.
    ///
    /// Le runtime ne fabrique aucune identité : toute écriture qu'un agent
    /// proposerait repassera par cette API sous ce jeton, et les permissions
    /// seront revérifiées. C'est ce qui fait qu'une injection de prompt réussie
    /// ne donne rien de plus que ce que l'utilisateur pouvait déjà faire.
    /// </summary>
    private string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MISSING_CALLER_TOKEN");

        return header["Bearer ".Length..].Trim();
    }

    // ── Versions figées ───────────────────────────────────────────────────────

    public async Task<AgentVersion> PublishAsync(Guid tenantId, Guid agentId, Guid actorId, string? changeLog, CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(agentId, ct) ?? throw new KeyNotFoundException("Agent introuvable.");

        // Republier à l'identique créerait une version morte : le numéro
        // n'avance que sur `Agent.Update`, donc une deuxième publication sans
        // modification produirait un doublon de numéro.
        var existing = await versionRepo.FindAsync(agentId, agent.VersionNumber, ct);
        if (existing is not null)
            throw new InvalidOperationException("VERSION_ALREADY_PUBLISHED");

        var snapshot = BuildSnapshot(agent);
        var version  = AgentVersion.Create(
            tenantId, agentId, agent.VersionNumber,
            JsonSerializer.Serialize(snapshot, SnapshotJsonOptions),
            actorId, changeLog);

        await versionRepo.AddAsync(version, ct);
        await versionRepo.SaveAsync(ct);

        agent.Publish(actorId, version.Id);
        agentRepo.Update(agent);
        await agentRepo.SaveAsync(ct);

        return version;
    }

    public async Task<AgentSnapshotDto> GetVersionSnapshotAsync(Guid agentId, string versionRef, CancellationToken ct = default)
    {
        // Le banc d'essai : l'instantané est construit depuis l'état courant, pas
        // relu d'une version figée. Rien n'est persisté — essayer un brouillon ne
        // doit pas créer de version que personne n'a demandée.
        if (string.Equals(versionRef, DraftVersionRef, StringComparison.OrdinalIgnoreCase))
        {
            var draft = await agentRepo.GetByIdAsync(agentId, ct)
                ?? throw new KeyNotFoundException("Agent introuvable.");

            return BuildSnapshot(draft);
        }

        var version = await ResolveVersionAsync(agentId, versionRef, ct)
            ?? throw new KeyNotFoundException("Version d'agent introuvable.");

        return JsonSerializer.Deserialize<AgentSnapshotDto>(version.SnapshotJson, SnapshotJsonOptions)
            ?? throw new InvalidOperationException("SNAPSHOT_CORRUPT");
    }

    public async Task<IReadOnlyList<AgentVersion>> ListVersionsAsync(Guid agentId, CancellationToken ct = default) =>
        await versionRepo.ListAsync(agentId, ct);

    /// <summary>
    /// Résout « 3 », « latest » ou « published ». Le runtime transmet la
    /// référence telle qu'elle figure dans la portée signée : c'est ici, et
    /// nulle part ailleurs, qu'elle s'interprète.
    /// </summary>
    private async Task<AgentVersion?> ResolveVersionAsync(Guid agentId, string versionRef, CancellationToken ct)
    {
        if (int.TryParse(versionRef, out var number))
            return await versionRepo.FindAsync(agentId, number, ct);

        if (string.Equals(versionRef, "latest", StringComparison.OrdinalIgnoreCase))
            return await versionRepo.FindLatestAsync(agentId, ct);

        if (string.Equals(versionRef, "published", StringComparison.OrdinalIgnoreCase))
        {
            var agent = await agentRepo.GetByIdAsync(agentId, ct);
            return agent?.PublishedVersionId is { } id ? await versionRepo.GetByIdAsync(id, ct) : null;
        }

        return null;
    }

    /// <summary>
    /// Traduit l'agent en instantané lisible par le runtime.
    ///
    /// Un seul point de traduction : si le domaine gagne un champ, il apparaît
    /// ici ou nulle part. Les instantanés déjà publiés, eux, ne changent pas —
    /// c'est tout l'intérêt de les figer.
    /// </summary>
    private static AgentSnapshotDto BuildSnapshot(Domain.Agent.Agent agent) => new(
        AgentId:                  agent.Id,
        Name:                     agent.Name,
        DisplayName:              agent.DisplayName,
        Type:                     agent.Type.ToString(),
        SystemPrompt:             agent.SystemPrompt,
        LlmConfigJson:            agent.LlmConfigJson,
        EnabledTools:             agent.EnabledTools,
        KnowledgePackIds:         agent.KnowledgePackIds,
        WorkspaceIds:             agent.WorkspaceIds,
        RequireHumanConfirmation: agent.RequireHumanConfirmation,
        MemoryEnabled:            agent.MemoryEnabled,
        MaxExecutionSeconds:      agent.MaxExecutionSeconds,
        VersionNumber:            agent.VersionNumber,
        SubAgents:                string.IsNullOrWhiteSpace(agent.SubAgentsJson)
                                      ? null
                                      : JsonSerializer.Deserialize<List<SubAgentRef>>(agent.SubAgentsJson, SnapshotJsonOptions));

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
