using EAIOS.Api.Domain.Workflow;

namespace EAIOS.Api.Application.Workflow;

public interface IWorkflowService
{
    // Définitions
    Task<WorkflowDefinition> CreateDefinitionAsync(Guid tenantId, string name, string? description, string? category, string nodesJson, Guid actorId, CancellationToken ct = default, string? scheduleCron = null);
    Task<WorkflowDefinition> UpdateDefinitionAsync(Guid id, string? name, string? description, string? category, string? nodesJson, CancellationToken ct = default, string? scheduleCron = null, bool clearSchedule = false);
    Task<WorkflowDefinition> PublishDefinitionAsync(Guid id, Guid actorId, CancellationToken ct = default);
    Task DeleteDefinitionAsync(Guid id, CancellationToken ct = default);

    // Instances
    Task<WorkflowInstance> StartInstanceAsync(Guid tenantId, Guid definitionId, WorkflowTriggerType triggerType, Guid actorId, Dictionary<string, object>? variables, DateTime? dueAt, CancellationToken ct = default);
    Task<WorkflowInstance> CancelInstanceAsync(Guid instanceId, CancellationToken ct = default);

    // Tâches
    Task<WorkflowTask> CompleteTaskAsync(Guid taskId, Guid actorId, string decision, string? comment, Dictionary<string, object>? formData, CancellationToken ct = default);
    Task<WorkflowTask> ReassignTaskAsync(Guid taskId, Guid newAssigneeId, CancellationToken ct = default);

    /// <summary>
    /// Ouvre une tache humaine autonome, hors instance de workflow.
    ///
    /// C'est ce dont depend l'outil `create_task` du runtime : un agent qui
    /// constate une incoherence ouvre une tache, sans qu'un workflow soit en
    /// cours. La decision humaine reste prealable — l'outil s'arrete avant
    /// d'appeler ici.
    /// </summary>
    Task<WorkflowTask> CreateStandaloneTaskAsync(
        Guid tenantId, Guid actorId, string title, string? instructions, string taskType,
        Guid? assigneeId, DateTime? dueAt, CancellationToken ct = default);
}

/// <summary>
/// Reprise d'une instance après qu'un nœud « agent » a abouti — y compris après
/// une décision humaine rendue des heures plus tard. Interface séparée pour que
/// le service des exécutions puisse l'appeler sans dépendance circulaire.
/// </summary>
public interface IWorkflowAgentContinuation
{
    Task ContinueAfterAgentAsync(Guid instanceId, EAIOS.Api.Domain.Agent.AgentExecution execution, CancellationToken ct = default);
}
