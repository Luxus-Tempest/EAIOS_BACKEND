using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Workflow;
using System.Text.Json;

namespace EAIOS.Api.Application.Workflow;

public sealed class WorkflowService(
    IWorkflowDefinitionRepository definitionRepo,
    IWorkflowInstanceRepository instanceRepo,
    IWorkflowTaskRepository taskRepo) : IWorkflowService
{
    public async Task<WorkflowDefinition> CreateDefinitionAsync(Guid tenantId, string name, string? description, string? category, string nodesJson, Guid actorId, CancellationToken ct = default)
    {
        var def = WorkflowDefinition.Create(tenantId, name, actorId, description, nodesJson);
        
        await definitionRepo.AddAsync(def, ct);
        await definitionRepo.SaveAsync(ct);
        
        return def;
    }

    public async Task<WorkflowDefinition> UpdateDefinitionAsync(Guid id, string? name, string? description, string? category, string? nodesJson, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Définition introuvable.");
        
        def.Update(name, description, nodesJson, category);
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);
        
        return def;
    }

    public async Task<WorkflowDefinition> PublishDefinitionAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Définition introuvable.");
        
        var versionId = Guid.CreateVersion7();
        def.Publish(versionId, "1.0.0"); // TODO: Gérer l'incrémentation de version dynamique
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);
        
        return def;
    }

    public async Task DeleteDefinitionAsync(Guid id, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Définition introuvable.");
        
        definitionRepo.SoftDelete(def);
        await definitionRepo.SaveAsync(ct);
    }

    public async Task<WorkflowInstance> StartInstanceAsync(Guid tenantId, Guid definitionId, WorkflowTriggerType triggerType, Guid actorId, Dictionary<string, object>? variables, DateTime? dueAt, CancellationToken ct = default)
    {
        var def = await definitionRepo.GetByIdAsync(definitionId, ct) ?? throw new KeyNotFoundException("Définition introuvable.");
        
        if (def.Status != WorkflowDefinitionStatus.Published)
            throw new InvalidOperationException("Ce workflow doit être publié avant exécution.");

        var variablesJson = variables != null ? JsonSerializer.Serialize(variables) : "{}";
        var instance = WorkflowInstance.Create(tenantId, definitionId, def.PublishedVersionId ?? Guid.Empty, def.Version, triggerType, actorId, variablesJson, dueAt);
        instance.Start("start"); // TODO: Trouver le premier nœud du graph
        
        await instanceRepo.AddAsync(instance, ct);
        await instanceRepo.SaveAsync(ct);
        
        return instance;
    }

    public async Task<WorkflowInstance> CancelInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct) ?? throw new KeyNotFoundException("Instance introuvable.");
        
        instance.Cancel();
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);
        
        return instance;
    }

    public async Task<WorkflowTask> CompleteTaskAsync(Guid taskId, Guid actorId, string decision, string? comment, Dictionary<string, object>? formData, CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(taskId, ct) ?? throw new KeyNotFoundException("Tâche introuvable.");
        
        if (task.AssigneeId != actorId)
            throw new UnauthorizedAccessException("Vous n'êtes pas assigné à cette tâche.");
            
        var formDataJson = formData != null ? JsonSerializer.Serialize(formData) : null;
        task.Complete(actorId, decision, comment, formDataJson);
        
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);
        
        return task;
    }

    public async Task<WorkflowTask> ReassignTaskAsync(Guid taskId, Guid newAssigneeId, CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(taskId, ct) ?? throw new KeyNotFoundException("Tâche introuvable.");
        
        task.Reassign(newAssigneeId, WorkflowTaskAssigneeType.User);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);
        
        return task;
    }
}
