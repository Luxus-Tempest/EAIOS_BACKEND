using EAIOS.Api.Application.Connector;
using EAIOS.Api.Application.Notification;
using EAIOS.Api.Application.Realtime;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Workflow;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EAIOS.Api.Application.Workflow;

/// <summary>
/// Pilotage des workflows : versionnage des définitions et exécution pas-à-pas
/// des instances sur le graphe publié.
///
/// Le moteur avance automatiquement à travers les étapes non-humaines et
/// s'arrête dès qu'une tâche humaine est requise ou qu'un noeud terminal est atteint.
/// </summary>
public sealed class WorkflowService(
    IWorkflowDefinitionRepository definitionRepo,
    IWorkflowInstanceRepository instanceRepo,
    IWorkflowTaskRepository taskRepo,
    EaiosDbContext db,
    IAnalyticsTracker analytics,
    EAIOS.Api.Application.Agent.IAgentExecutionService agentExecutions,
    ILogger<WorkflowService> logger,
    INotificationDispatcher dispatcher,
    IRealtimeEventService realtime,
    EAIOS.Api.Application.Agent.IAgentService agentService) : IWorkflowService, IWorkflowAgentContinuation
{
    /// <summary>Garde-fou anti-boucle : un cycle dans le graphe ne doit pas tourner indéfiniment.</summary>
    private const int MaxStepsPerAdvance = 100;

    // ═════════════════════════════════════════════════════════════════════════
    // DÉFINITIONS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<WorkflowDefinition> CreateDefinitionAsync(
        Guid tenantId, string name, string? description, string? category,
        string nodesJson, Guid actorId, CancellationToken ct = default, string? scheduleCron = null)
    {
        var def = WorkflowDefinition.Create(tenantId, name, actorId, description, nodesJson);

        if (!string.IsNullOrWhiteSpace(category))
            def.Update(null, null, null, category);

        ApplySchedule(def, scheduleCron);

        await definitionRepo.AddAsync(def, ct);
        await definitionRepo.SaveAsync(ct);

        return def;
    }

    public async Task<WorkflowDefinition> UpdateDefinitionAsync(
        Guid id, string? name, string? description, string? category,
        string? nodesJson, CancellationToken ct = default, string? scheduleCron = null, bool clearSchedule = false)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Définition introuvable.");

        // L'horaire se règle sans repasser en brouillon : ce n'est pas le graphe qui change.
        if (clearSchedule) def.SetSchedule(null, null);
        else if (scheduleCron is not null) ApplySchedule(def, scheduleCron);

        if (name is not null || description is not null || nodesJson is not null || category is not null)
            def.Update(name, description, nodesJson, category);

        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);

        return def;
    }

    /// <summary>Valide l'expression à l'écriture, et pose la prochaine échéance.</summary>
    private static void ApplySchedule(WorkflowDefinition def, string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) { def.SetSchedule(null, null); return; }
        if (!CronSchedule.TryParse(cron, out var schedule) || schedule is null)
            throw new ArgumentException($"Expression cron invalide : « {cron} ». Format attendu : « minute heure jour mois jour-semaine ».");
        def.SetSchedule(cron, schedule.GetNextOccurrence(DateTime.UtcNow));
    }

    /// <summary>
    /// Publie la définition : valide le graphe, fige une nouvelle
    /// <see cref="WorkflowDefinitionVersion"/> immuable et incrémente le numéro de
    /// version sémantique en fonction de l'historique réel (plus de « 1.0.0 » figé).
    /// </summary>
    public async Task<WorkflowDefinition> PublishDefinitionAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Définition introuvable.");

        var graph  = WorkflowGraph.Parse(def.GraphJson);
        var errors = graph.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Le graphe du workflow est invalide : " + string.Join(" ", errors));

        // Le prochain numéro se déduit des versions déjà publiées.
        var lastVersionNumber = await db.WorkflowDefinitionVersions
            .Where(v => v.DefinitionId == id)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var nextNumber = lastVersionNumber + 1;
        var nextLabel  = $"{nextNumber}.0.0";

        var version = WorkflowDefinitionVersion.Create(
            def.OrganizationId, def.Id, nextNumber, nextLabel,
            def.GraphJson ?? "{}", actorId,
            changeLog: $"Publication de la version {nextLabel}.");

        await db.WorkflowDefinitionVersions.AddAsync(version, ct);

        def.Publish(version.Id, nextLabel);
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);

        logger.LogInformation("Workflow {DefinitionId} publié en version {Version}.", def.Id, nextLabel);
        return def;
    }

    public async Task DeleteDefinitionAsync(Guid id, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Définition introuvable.");

        // Supprimer une définition dont des instances tournent encore les rendrait orphelines.
        var running = await db.WorkflowInstances.CountAsync(
            i => i.DefinitionId == id
              && (i.Status == WorkflowInstanceStatus.Executing
               || i.Status == WorkflowInstanceStatus.WaitingForApproval
               || i.Status == WorkflowInstanceStatus.Paused
               || i.Status == WorkflowInstanceStatus.Initialized), ct);

        if (running > 0)
            throw new InvalidOperationException(
                $"Impossible de supprimer ce workflow : {running} instance(s) encore en cours. Annulez-les d'abord.");

        definitionRepo.SoftDelete(def);
        await definitionRepo.SaveAsync(ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INSTANCES
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<WorkflowInstance> StartInstanceAsync(
        Guid tenantId, Guid definitionId, WorkflowTriggerType triggerType, Guid actorId,
        Dictionary<string, object>? variables, DateTime? dueAt, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(definitionId, ct)
            ?? throw new KeyNotFoundException("Définition introuvable.");

        if (def.Status != WorkflowDefinitionStatus.Published)
            throw new InvalidOperationException("Ce workflow doit être publié avant exécution.");

        // Exécuter la version publiée figée, pas le brouillon courant : une
        // instance doit rester fidèle au graphe en vigueur à son démarrage.
        var publishedVersion = def.PublishedVersionId.HasValue
            ? await db.WorkflowDefinitionVersions.FirstOrDefaultAsync(v => v.Id == def.PublishedVersionId.Value, ct)
            : null;

        var graphJson = publishedVersion?.GraphJson ?? def.GraphJson;
        var graph     = WorkflowGraph.Parse(graphJson);

        var startNode = graph.FindStartNode()
            ?? throw new InvalidOperationException("Le graphe publié ne contient aucun noeud de départ exploitable.");

        var variablesJson = variables is not null ? JsonSerializer.Serialize(variables) : "{}";

        var instance = WorkflowInstance.Create(
            tenantId, definitionId,
            def.PublishedVersionId ?? Guid.Empty, def.Version,
            triggerType, actorId, variablesJson, dueAt);

        instance.Start(startNode.Id);

        await instanceRepo.AddAsync(instance, ct);
        await instanceRepo.SaveAsync(ct);

        def.IncrementExecutionCount();
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);

        // Traverse les étapes automatiques jusqu'à la première tâche humaine ou la fin.
        await AdvanceAsync(instance, graph, startNode, decision: null, ct);

        await analytics.TrackAsync(AnalyticsEventTypes.WorkflowStarted,
            resourceId: instance.Id, resourceType: "WorkflowInstance",
            properties: new { definitionId, workflow = def.Name }, ct: ct);

        return instance;
    }

    public async Task<WorkflowInstance> CancelInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new KeyNotFoundException("Instance introuvable.");

        if (instance.Status is WorkflowInstanceStatus.Completed
                            or WorkflowInstanceStatus.Cancelled
                            or WorkflowInstanceStatus.Failed)
            throw new InvalidOperationException("Cette instance est déjà terminée.");

        instance.Cancel();
        instanceRepo.Update(instance);

        // Les tâches encore ouvertes doivent disparaître des boîtes de réception.
        var openTasks = await db.WorkflowTasks
            .Where(t => t.InstanceId == instanceId
                     && (t.Status == WorkflowTaskStatus.Open
                      || t.Status == WorkflowTaskStatus.InProgress
                      || t.Status == WorkflowTaskStatus.Escalated))
            .ToListAsync(ct);

        foreach (var task in openTasks)
            task.Cancel();

        await instanceRepo.SaveAsync(ct);

        logger.LogInformation("Instance {InstanceId} annulée — {Count} tâche(s) close(s).", instanceId, openTasks.Count);
        return instance;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TÂCHES
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ouvre une tâche humaine autonome, hors instance de workflow.
    ///
    /// <para>
    /// Le type est validé contre une liste close : `TaskType` est une chaîne
    /// libre dans le domaine, et l'écran d'approbation s'appuie dessus pour
    /// choisir sa forme. Une valeur inventée y produirait une tâche sans
    /// interface.
    /// </para>
    /// </summary>
    public async Task<WorkflowTask> CreateStandaloneTaskAsync(
        Guid tenantId, Guid actorId, string title, string? instructions, string taskType,
        Guid? assigneeId, DateTime? dueAt, CancellationToken ct = default)
    {
        if (!StandaloneTaskTypes.Contains(taskType))
            throw new InvalidOperationException(
                $"Type de tâche « {taskType} » inconnu. Attendus : {string.Join(", ", StandaloneTaskTypes)}.");

        var task = WorkflowTask.ForAgentDecision(
            organizationId:   tenantId,
            // Pas d'exécution derrière : cette tâche est ouverte par une
            // personne ou par un agent déjà autorisé, pas par un arrêt. Clore la
            // tâche ne reprendra donc rien.
            agentExecutionId: null,
            taskType:         taskType,
            title:            title,
            instructions:     instructions,
            assigneeId:       assigneeId ?? actorId,
            dueAt:            dueAt);

        await taskRepo.AddAsync(task, ct);
        await taskRepo.SaveAsync(ct);

        logger.LogInformation("Tâche autonome {TaskId} ouverte par {ActorId}.", task.Id, actorId);

        if (task.AssigneeId != actorId)
            await NotifyTaskAsync(task, "task.assigned", $"Nouvelle tâche : {task.Title}", instructions, ct);
        return task;
    }

    /// <summary>Types acceptés, alignés sur `WorkflowTask.TaskType` et sur l'écran d'approbation.</summary>
    private static readonly HashSet<string> StandaloneTaskTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Approval", "Review", "DataInput" };

    /// <summary>
    /// Clôt une tâche humaine puis fait avancer l'instance sur la branche
    /// correspondant à la décision prise.
    /// </summary>
    public async Task<WorkflowTask> CompleteTaskAsync(
        Guid taskId, Guid actorId, string decision, string? comment,
        Dictionary<string, object>? formData, CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(taskId, ct)
            ?? throw new KeyNotFoundException("Tâche introuvable.");

        if (task.Status is WorkflowTaskStatus.Completed or WorkflowTaskStatus.Cancelled)
            throw new InvalidOperationException("Cette tâche est déjà close.");

        if (task.AssigneeId.HasValue && task.AssigneeId != actorId)
            throw new UnauthorizedAccessException("Vous n'êtes pas assigné à cette tâche.");

        var formDataJson = formData is not null ? JsonSerializer.Serialize(formData) : null;
        task.Complete(actorId, decision, comment, formDataJson);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);

        // Une tâche née d'un arrêt d'agent n'appartient à aucune instance : ce
        // n'est pas un workflow qu'il faut faire avancer, c'est une exécution
        // qu'il faut reprendre. Le runtime repartira exactement au point d'arrêt.
        if (task.AgentExecutionId is { } executionId)
        {
            await agentExecutions.SubmitHumanInputAsync(executionId, decision, comment, ct);
            return task;
        }

        if (task.InstanceId is not { } instanceId)
        {
            logger.LogWarning("Tâche {TaskId} close sans origine : ni instance de workflow, ni exécution d'agent.", taskId);
            return task;
        }

        var instance = await instanceRepo.GetByIdAsync(instanceId, ct);
        if (instance is null)
        {
            logger.LogWarning("Tâche {TaskId} close mais instance {InstanceId} introuvable.", taskId, instanceId);
            return task;
        }

        if (instance.Status is WorkflowInstanceStatus.Cancelled
                            or WorkflowInstanceStatus.Completed
                            or WorkflowInstanceStatus.Failed)
            return task;

        var graph = await LoadGraphAsync(instance, ct);

        // Les données du formulaire enrichissent les variables de l'instance :
        // les noeuds de condition en aval peuvent ainsi s'appuyer dessus.
        if (formData is { Count: > 0 })
            MergeVariables(instance, formData);

        var currentNode = graph.GetNode(task.StepId);
        if (currentNode is null)
        {
            logger.LogWarning("Étape « {StepId} » absente du graphe de l'instance {InstanceId}.", task.StepId, instance.Id);
            return task;
        }

        var nextNode = graph.GetNextNode(currentNode.Id, decision);
        if (nextNode is null)
        {
            // Plus aucune transition : l'étape close termine le workflow.
            instance.Complete();
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);
            await TrackCompletionAsync(instance, ct);
            return task;
        }

        instance.Resume(nextNode.Id);
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);

        await AdvanceAsync(instance, graph, nextNode, decision, ct);
        return task;
    }

    public async Task<WorkflowTask> ReassignTaskAsync(Guid taskId, Guid newAssigneeId, CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(taskId, ct)
            ?? throw new KeyNotFoundException("Tâche introuvable.");

        if (task.Status is WorkflowTaskStatus.Completed or WorkflowTaskStatus.Cancelled)
            throw new InvalidOperationException("Une tâche close ne peut pas être réassignée.");

        task.Reassign(newAssigneeId, WorkflowTaskAssigneeType.User);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);

        await NotifyTaskAsync(task, "task.assigned", $"Tâche réassignée : {task.Title}", task.Instructions, ct);
        return task;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MOTEUR
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fait progresser l'instance depuis <paramref name="node"/> : traverse les étapes
    /// automatiques et les branchements, crée la tâche quand une intervention humaine
    /// est requise, et clôt l'instance sur un noeud terminal.
    /// </summary>
    private async Task AdvanceAsync(
        WorkflowInstance instance, WorkflowGraph graph, WorkflowNode node,
        string? decision, CancellationToken ct)
    {
        var current = node;
        var steps   = 0;

        while (current is not null)
        {
            if (++steps > MaxStepsPerAdvance)
            {
                instance.Fail($"Arrêt de sécurité : plus de {MaxStepsPerAdvance} étapes traversées sans point d'arrêt (cycle probable dans le graphe).");
                instanceRepo.Update(instance);
                await instanceRepo.SaveAsync(ct);
                logger.LogError("Instance {InstanceId} interrompue — cycle suspecté dans le graphe.", instance.Id);
                return;
            }

            var type = current.NormalizedType;

            // ── Fin du workflow ───────────────────────────────────────────────
            if (WorkflowNodeTypes.IsTerminal(type))
            {
                instance.Complete();
                instanceRepo.Update(instance);
                await instanceRepo.SaveAsync(ct);
                await TrackCompletionAsync(instance, ct);
                logger.LogInformation("Instance {InstanceId} terminée sur le noeud « {NodeId} ».", instance.Id, current.Id);
                return;
            }

            // ── Tâche humaine : on s'arrête et on attend ──────────────────────
            if (WorkflowNodeTypes.IsHumanTask(type))
            {
                await CreateTaskAsync(instance, current, ct);

                instance.WaitForApproval(current.Id);
                instanceRepo.Update(instance);
                await instanceRepo.SaveAsync(ct);
                return;
            }

            // ── Nœud agent : l'IA fait l'étape, sous contrôle humain ──────────
            if (type == WorkflowNodeTypes.Agent)
            {
                var outcome = await RunAgentNodeAsync(instance, current, ct);
                if (outcome != AgentNodeOutcome.Completed)
                {
                    // En attente d'une décision humaine (l'instance reprendra par
                    // ContinueAfterAgentAsync) ou en échec : l'état est déjà posé.
                    instanceRepo.Update(instance);
                    await instanceRepo.SaveAsync(ct);
                    return;
                }
            }

            // ── Branchement conditionnel ──────────────────────────────────────
            string? branch = null;
            if (type == WorkflowNodeTypes.Condition)
                branch = EvaluateCondition(instance, current);

            // ── Étape automatique : on traverse ───────────────────────────────
            var next = graph.GetNextNode(current.Id, branch ?? (steps == 1 ? decision : null));

            if (next is null)
            {
                instance.Complete();
                instanceRepo.Update(instance);
                await instanceRepo.SaveAsync(ct);
                await TrackCompletionAsync(instance, ct);
                return;
            }

            instance.Resume(next.Id);
            current = next;
        }

        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);
    }

    private enum AgentNodeOutcome { Completed, Waiting, Failed }

    /// <summary>
    /// Exécute l'agent d'un nœud « agent » avec ses instructions (rendues avec
    /// les variables de l'instance) et range sa réponse dans les variables sous
    /// l'identifiant du nœud. Un arrêt pour décision humaine met l'instance en
    /// attente ; la reprise passe par <see cref="ContinueAfterAgentAsync"/>.
    /// </summary>
    private async Task<AgentNodeOutcome> RunAgentNodeAsync(WorkflowInstance instance, WorkflowNode node, CancellationToken ct)
    {
        if (node.AgentId is not { } agentId)
        {
            instance.Fail($"Le nœud « {node.DisplayLabel} » est de type agent mais ne désigne aucun agent (agentId).");
            return AgentNodeOutcome.Failed;
        }

        var actorId = instance.TriggeredBy ?? (await definitionRepo.GetByIdAsync(instance.DefinitionId, ct))?.OwnerId;
        if (actorId is null)
        {
            instance.Fail("Impossible de déterminer au nom de qui l'agent doit agir.");
            return AgentNodeOutcome.Failed;
        }

        var input = RenderTemplate(node.Instructions ?? node.DisplayLabel, instance);

        AgentExecution execution;
        try
        {
            execution = await agentService.ExecuteAsync(instance.OrganizationId, agentId, input, actorId.Value, null, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "MISSING_CALLER_TOKEN")
        {
            // Un lancement planifié n'a pas de personne connectée derrière lui, et
            // le runtime n'exécute rien sans le jeton d'une personne réelle.
            instance.Fail("Un nœud « agent » ne s'exécute pas dans un lancement planifié : il faut une personne connectée. Lancez ce workflow à la main.");
            return AgentNodeOutcome.Failed;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            instance.Fail($"L'agent du nœud « {node.DisplayLabel} » n'a pas pu être exécuté : {ex.Message}");
            return AgentNodeOutcome.Failed;
        }

        execution.LinkToWorkflowInstance(instance.Id);
        await db.SaveChangesAsync(ct);

        return await ApplyAgentOutcome(instance, node, execution);
    }

    private static Task<AgentNodeOutcome> ApplyAgentOutcome(WorkflowInstance instance, WorkflowNode node, AgentExecution execution)
    {
        switch (execution.Status)
        {
            case AgentExecutionStatus.Completed:
                MergeVariables(instance, new Dictionary<string, object>
                {
                    [node.Id] = execution.OutputText ?? "",
                    [$"{node.Id}_citations"] = execution.Citations ?? [],
                    [$"{node.Id}_executionId"] = execution.Id.ToString(),
                });
                return Task.FromResult(AgentNodeOutcome.Completed);

            case AgentExecutionStatus.AwaitingHumanInput:
                instance.WaitForApproval(node.Id);
                return Task.FromResult(AgentNodeOutcome.Waiting);

            default:
                instance.Fail($"L'agent du nœud « {node.DisplayLabel} » a échoué : {execution.ErrorMessage ?? execution.Status.ToString()}");
                return Task.FromResult(AgentNodeOutcome.Failed);
        }
    }

    public async Task ContinueAfterAgentAsync(Guid instanceId, AgentExecution execution, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct);
        if (instance is null || instance.Status is not WorkflowInstanceStatus.WaitingForApproval) return;

        var graph = await LoadGraphAsync(instance, ct);
        var node = instance.CurrentStepId is null ? null : graph.GetNode(instance.CurrentStepId);
        if (node is null) return;

        var outcome = await ApplyAgentOutcome(instance, node, execution);
        if (outcome != AgentNodeOutcome.Completed)
        {
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);
            return;
        }

        var next = graph.GetNextNode(node.Id, null);
        if (next is null)
        {
            instance.Complete();
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);
            await TrackCompletionAsync(instance, ct);
            return;
        }

        instance.Resume(next.Id);
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);
        await AdvanceAsync(instance, graph, next, null, ct);
    }

    /// <summary>« Analyse {{contrat}} » : les variables de l'instance remplacent les accolades.</summary>
    private static string RenderTemplate(string template, WorkflowInstance instance)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(instance.VariablesJson) ? "{}" : instance.VariablesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return template;
            var rendered = template;
            foreach (var prop in doc.RootElement.EnumerateObject())
                rendered = rendered.Replace("{{" + prop.Name + "}}", prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString(), StringComparison.OrdinalIgnoreCase);
            return rendered;
        }
        catch (JsonException)
        {
            return template;
        }
    }

    private async Task CreateTaskAsync(WorkflowInstance instance, WorkflowNode node, CancellationToken ct)
    {
        // Ne pas recréer une tâche déjà ouverte pour cette étape (relance, reprise).
        var alreadyOpen = await db.WorkflowTasks.AnyAsync(
            t => t.InstanceId == instance.Id
              && t.StepId == node.Id
              && (t.Status == WorkflowTaskStatus.Open || t.Status == WorkflowTaskStatus.InProgress), ct);

        if (alreadyOpen) return;

        var assigneeType = ParseAssigneeType(node.AssigneeType);
        var dueAt = node.DueInHours.HasValue
            ? DateTime.UtcNow.AddHours(node.DueInHours.Value)
            : instance.DueAt;

        var task = WorkflowTask.Create(
            instance.OrganizationId,
            instance.Id,
            node.Id,
            taskType:     MapTaskType(node.NormalizedType),
            title:        node.DisplayLabel,
            assigneeType: assigneeType,
            assigneeId:   node.AssigneeId,
            dueAt:        dueAt);

        await taskRepo.AddAsync(task, ct);
        await taskRepo.SaveAsync(ct);

        logger.LogInformation("Tâche « {Title} » créée pour l'instance {InstanceId} (étape {StepId}).",
            task.Title, instance.Id, node.Id);

        await NotifyTaskAsync(task, "task.assigned", $"Nouvelle tâche : {task.Title}",
            node.Instructions ?? "Une étape de workflow attend votre décision.", ct);
    }

    /// <summary>La personne assignée est prévenue ; la file « Tâches » se rafraîchit en direct.</summary>
    private async Task NotifyTaskAsync(WorkflowTask task, string eventType, string title, string? body, CancellationToken ct)
    {
        if (task.AssigneeId is { } assignee)
            await dispatcher.DispatchAsync(new NotificationRequest(task.OrganizationId, assignee, eventType, title, body,
                "/tasks", "Traiter", task.DueAt.HasValue ? NotificationPriority.High : NotificationPriority.Normal,
                new { taskId = task.Id, instanceId = task.InstanceId }), ct);

        await realtime.PublishToTenantAsync(task.OrganizationId, "workflow.task", new { taskId = task.Id, status = task.Status.ToString() });
    }

    /// <summary>
    /// Évalue un noeud de condition : renvoie la valeur de la variable désignée,
    /// telle qu'elle sera comparée aux conditions portées par les arêtes sortantes.
    /// </summary>
    private static string? EvaluateCondition(WorkflowInstance instance, WorkflowNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Variable)) return null;

        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(instance.VariablesJson) ? "{}" : instance.VariablesJson);

            if (!doc.RootElement.TryGetProperty(node.Variable, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True   => "true",
                JsonValueKind.False  => "false",
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.Null   => null,
                _                    => value.ToString()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Fusionne de nouvelles valeurs dans les variables de l'instance.</summary>
    private static void MergeVariables(WorkflowInstance instance, Dictionary<string, object> additions)
    {
        var merged = new Dictionary<string, object?>();

        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(instance.VariablesJson) ? "{}" : instance.VariablesJson);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    merged[prop.Name] = JsonElementToObject(prop.Value);
            }
        }
        catch (JsonException)
        {
            // Variables illisibles : on repart des seules nouvelles valeurs.
        }

        foreach (var (key, value) in additions)
            merged[key] = value;

        instance.UpdateVariables(JsonSerializer.Serialize(merged));
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Null   => null,
        _                    => element.ToString()
    };

    private async Task<WorkflowGraph> LoadGraphAsync(WorkflowInstance instance, CancellationToken ct)
    {
        if (instance.DefinitionVersionId != Guid.Empty)
        {
            var version = await db.WorkflowDefinitionVersions
                .FirstOrDefaultAsync(v => v.Id == instance.DefinitionVersionId, ct);

            if (version is not null) return WorkflowGraph.Parse(version.GraphJson);
        }

        var def = await definitionRepo.GetByIdAsync(instance.DefinitionId, ct);
        return WorkflowGraph.Parse(def?.GraphJson);
    }

    private async Task TrackCompletionAsync(WorkflowInstance instance, CancellationToken ct)
    {
        await analytics.TrackAsync(AnalyticsEventTypes.WorkflowCompleted,
            resourceId: instance.Id, resourceType: "WorkflowInstance",
            durationMs: (long)(DateTime.UtcNow - instance.StartedAt).TotalMilliseconds,
            properties: new { definitionId = instance.DefinitionId }, ct: ct);

        if (instance.TriggeredBy is { } starter)
            await dispatcher.DispatchAsync(new NotificationRequest(instance.OrganizationId, starter, "workflow.completed",
                "Workflow terminé", "Toutes les étapes ont abouti.",
                $"/workflows/{instance.DefinitionId}", "Voir le workflow"), ct);

        await realtime.PublishToTenantAsync(instance.OrganizationId, "workflow.completed", new { instanceId = instance.Id });
    }

    private static string MapTaskType(string nodeType) => nodeType switch
    {
        WorkflowNodeTypes.Approval  => "Approval",
        WorkflowNodeTypes.Review    => "Review",
        WorkflowNodeTypes.DataInput => "DataInput",
        _                           => "Task"
    };

    private static WorkflowTaskAssigneeType ParseAssigneeType(string? type) =>
        (type ?? "user").Trim().ToLowerInvariant() switch
        {
            "department" => WorkflowTaskAssigneeType.Department,
            "role"       => WorkflowTaskAssigneeType.Role,
            "workspace"  => WorkflowTaskAssigneeType.Workspace,
            "agent"      => WorkflowTaskAssigneeType.Agent,
            _            => WorkflowTaskAssigneeType.User
        };
}
