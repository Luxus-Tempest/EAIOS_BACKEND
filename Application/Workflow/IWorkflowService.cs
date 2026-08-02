using EAIOS.Api.Domain.Workflow;

namespace EAIOS.Api.Application.Workflow;

public interface IWorkflowService
{
    // Définitions
    Task<WorkflowDefinition> CreateDefinitionAsync(Guid tenantId, string name, string? description, string? category, string nodesJson, Guid actorId, CancellationToken ct = default);
    Task<WorkflowDefinition> UpdateDefinitionAsync(Guid id, string? name, string? description, string? category, string? nodesJson, CancellationToken ct = default);
    Task<WorkflowDefinition> PublishDefinitionAsync(Guid id, Guid actorId, CancellationToken ct = default);
    Task DeleteDefinitionAsync(Guid id, CancellationToken ct = default);

    // Instances
    Task<WorkflowInstance> StartInstanceAsync(Guid tenantId, Guid definitionId, WorkflowTriggerType triggerType, Guid actorId, Dictionary<string, object>? variables, DateTime? dueAt, CancellationToken ct = default);
    Task<WorkflowInstance> CancelInstanceAsync(Guid instanceId, CancellationToken ct = default);

    // Tâches
    Task<WorkflowTask> CompleteTaskAsync(Guid taskId, Guid actorId, string decision, string? comment, Dictionary<string, object>? formData, CancellationToken ct = default);
    Task<WorkflowTask> ReassignTaskAsync(Guid taskId, Guid newAssigneeId, CancellationToken ct = default);
}
