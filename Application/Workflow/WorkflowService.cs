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
    ILogger<WorkflowService> logger) : IWorkflowService
{
    /// <summary>Garde-fou anti-boucle : un cycle dans le graphe ne doit pas tourner indéfiniment.</summary>
    private const int MaxStepsPerAdvance = 100;

    // ═════════════════════════════════════════════════════════════════════════
    // DÉFINITIONS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<WorkflowDefinition> CreateDefinitionAsync(
        Guid tenantId, string name, string? description, string? category,
        string nodesJson, Guid actorId, CancellationToken ct = default)
    {
        var def = WorkflowDefinition.Create(tenantId, name, actorId, description, nodesJson);

        if (!string.IsNullOrWhiteSpace(category))
            def.Update(null, null, null, category);

        await definitionRepo.AddAsync(def, ct);
        await definitionRepo.SaveAsync(ct);

        return def;
    }

    public async Task<WorkflowDefinition> UpdateDefinitionAsync(
        Guid id, string? name, string? description, string? category,
        string? nodesJson, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Définition introuvable.");

        def.Update(name, description, nodesJson, category);
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);

        return def;
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

        var instance = await instanceRepo.GetByIdAsync(task.InstanceId, ct);
        if (instance is null)
        {
            logger.LogWarning("Tâche {TaskId} close mais instance {InstanceId} introuvable.", taskId, task.InstanceId);
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

            // ── Branchement conditionnel ──────────────────────────────────────
            string? branch = null;
            if (type == WorkflowNodeTypes.Condition)
                branch = EvaluateCondition(instance, current);

            // ── Étape automatique / agent : on traverse ───────────────────────
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

    private Task TrackCompletionAsync(WorkflowInstance instance, CancellationToken ct) =>
        analytics.TrackAsync(AnalyticsEventTypes.WorkflowCompleted,
            resourceId: instance.Id, resourceType: "WorkflowInstance",
            durationMs: (long)(DateTime.UtcNow - instance.StartedAt).TotalMilliseconds,
            properties: new { definitionId = instance.DefinitionId }, ct: ct);

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
