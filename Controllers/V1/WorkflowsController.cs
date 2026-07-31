using EAIOS.Api.Application.Workflow;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Workflows : définitions, instances, tâches.
/// </summary>
[Route("api/v1/workflows")]
public sealed class WorkflowsController(
    IWorkflowDefinitionRepository definitionRepo,
    IWorkflowInstanceRepository   instanceRepo,
    IWorkflowTaskRepository       taskRepo) : V1ApiController
{
    // ── Définitions ───────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListDefinitions(
        [FromQuery] string? q,
        [FromQuery] WorkflowDefinitionStatus? status,
        [FromQuery] string? category,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await definitionRepo.SearchAsync(q, status, category, page, pageSize, ct);
        return OkList(result.Items.Select(MapDefinition).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("{id:guid}", Name = "GetWorkflowDefinition")]
    public async Task<IActionResult> GetDefinition(Guid id, CancellationToken ct)
    {
        var def = await definitionRepo.GetWithVersionsAsync(id, ct);
        return def == null ? NotFound() : Ok200(MapDefinition(def));
    }

    [HttpPost]
    public async Task<IActionResult> CreateDefinition([FromBody] CreateWorkflowDefinitionRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var def = WorkflowDefinition.Create(TenantId, req.Name, ActorId.Value, req.Description, req.NodesJson);

        await definitionRepo.AddAsync(def, ct);
        await definitionRepo.SaveAsync(ct);
        return Created201("GetWorkflowDefinition", new { id = def.Id }, MapDefinition(def));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDefinition(Guid id, [FromBody] UpdateWorkflowDefinitionRequest req, CancellationToken ct)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct);
        if (def == null) return NotFound();
        def.Update(req.Name, req.Description, req.NodesJson, req.Category);
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);
        return Ok200(MapDefinition(def));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var def = await definitionRepo.GetByIdAsync(id, ct);
        if (def == null) return NotFound();
        var versionId = Guid.CreateVersion7();
        def.Publish(versionId, "1.0.0");
        definitionRepo.Update(def);
        await definitionRepo.SaveAsync(ct);
        return Ok200(MapDefinition(def));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDefinition(Guid id, CancellationToken ct)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct);
        if (def == null) return NotFound();
        definitionRepo.SoftDelete(def);
        await definitionRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Instances ─────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/instances")]
    public async Task<IActionResult> ListInstances(
        Guid id,
        [FromQuery] WorkflowInstanceStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await instanceRepo.SearchAsync(id, status, page, pageSize, ct);
        return OkList(result.Items.Select(MapInstance).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpPost("{id:guid}/instances")]
    public async Task<IActionResult> StartInstance(Guid id, [FromBody] StartWorkflowRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var def = await definitionRepo.GetByIdAsync(id, ct);
        if (def == null) return NotFound();
        if (def.Status != WorkflowDefinitionStatus.Published)
            return UnprocessableEntity("Ce workflow doit être publié avant exécution.");

        var variablesJson = req.Variables != null ? System.Text.Json.JsonSerializer.Serialize(req.Variables) : "{}";
        var instance = WorkflowInstance.Create(TenantId, id, def.PublishedVersionId ?? Guid.Empty, def.Version, req.TriggerType, ActorId.Value, variablesJson, req.DueAt);
        instance.Start("start");
        await instanceRepo.AddAsync(instance, ct);
        await instanceRepo.SaveAsync(ct);
        return Ok200(MapInstance(instance));
    }

    [HttpGet("instances/{instanceId:guid}")]
    public async Task<IActionResult> GetInstance(Guid instanceId, CancellationToken ct)
    {
        var instance = await instanceRepo.GetWithTasksAsync(instanceId, ct);
        return instance == null ? NotFound() : Ok200(MapInstance(instance));
    }

    [HttpPost("instances/{instanceId:guid}/cancel")]
    public async Task<IActionResult> CancelInstance(Guid instanceId, [FromBody] CancelWorkflowRequest req, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct);
        if (instance == null) return NotFound();
        instance.Cancel();
        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);
        return Ok200(MapInstance(instance));
    }

    // ── Tâches ────────────────────────────────────────────────────────────────

    [HttpGet("tasks")]
    public async Task<IActionResult> GetMyTasks(
        [FromQuery] WorkflowTaskStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var result = await taskRepo.GetAssignedToUserAsync(ActorId.Value, status, page, pageSize, ct);
        return OkList(result.Items.Select(MapTask).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpPost("tasks/{taskId:guid}/complete")]
    public async Task<IActionResult> CompleteTask(Guid taskId, [FromBody] CompleteTaskRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var task = await taskRepo.GetByIdAsync(taskId, ct);
        if (task == null) return NotFound();
        if (task.AssigneeId != ActorId.Value)
            return Forbidden("Vous n'êtes pas assigné à cette tâche.");
        var formDataJson = req.FormData != null ? System.Text.Json.JsonSerializer.Serialize(req.FormData) : null;
        task.Complete(ActorId.Value, req.Decision, req.Comment, formDataJson);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);
        return Ok200(MapTask(task));
    }

    [HttpPost("tasks/{taskId:guid}/reassign")]
    public async Task<IActionResult> ReassignTask(Guid taskId, [FromBody] ReassignTaskRequest req, CancellationToken ct)
    {
        var task = await taskRepo.GetByIdAsync(taskId, ct);
        if (task == null) return NotFound();
        task.Reassign(req.NewAssigneeId, EAIOS.Api.Domain.Workflow.WorkflowTaskAssigneeType.User);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);
        return Ok200(MapTask(task));
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapDefinition(WorkflowDefinition d) => new
    {
        d.Id, d.Name, d.Description, d.Category, d.Status,
        d.PublishedVersionId, d.CreatedAt, d.UpdatedAt
    };

    private static object MapInstance(WorkflowInstance i) => new
    {
        i.Id, i.DefinitionId, i.Status, i.TriggerType, i.StartedAt, i.CompletedAt,
        i.CurrentStepId, i.DueAt, i.CreatedAt
    };

    private static object MapTask(WorkflowTask t) => new
    {
        t.Id, t.InstanceId, t.StepId, t.Title, t.Instructions, t.Status, t.AssigneeId,
        t.AssigneeType, t.DueAt, t.CompletedAt, t.Decision, t.Comment, t.CreatedAt
    };
}
