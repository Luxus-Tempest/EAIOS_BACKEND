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
        var def = WorkflowDefinition.Create(TenantId, req.Name, req.Description, req.Category, ActorId.Value);
        def.SetNodes(req.NodesJson);
        def.SetEdges(req.EdgesJson);
        def.SetTrigger(req.TriggerType, req.TriggerConfig);

        await definitionRepo.AddAsync(def, ct);
        await definitionRepo.SaveAsync(ct);
        return Created201("GetWorkflowDefinition", new { id = def.Id }, MapDefinition(def));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDefinition(Guid id, [FromBody] UpdateWorkflowDefinitionRequest req, CancellationToken ct)
    {
        var def = await definitionRepo.GetByIdAsync(id, ct);
        if (def == null) return NotFound();
        def.Update(req.Name, req.Description, req.Category);
        if (req.NodesJson != null) def.SetNodes(req.NodesJson);
        if (req.EdgesJson != null) def.SetEdges(req.EdgesJson);
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
        def.Publish(ActorId.Value);
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

        var instance = WorkflowInstance.Start(TenantId, id, def.PublishedVersionId, WorkflowTriggerType.Manual, ActorId.Value, req.InputDataJson);
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
        instance.Cancel(req.Reason);
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
        task.Complete(req.Decision, req.Comment, req.OutputDataJson);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);
        return Ok200(MapTask(task));
    }

    [HttpPost("tasks/{taskId:guid}/reassign")]
    public async Task<IActionResult> ReassignTask(Guid taskId, [FromBody] ReassignTaskRequest req, CancellationToken ct)
    {
        var task = await taskRepo.GetByIdAsync(taskId, ct);
        if (task == null) return NotFound();
        task.Reassign(req.NewAssigneeId, req.Reason);
        taskRepo.Update(task);
        await taskRepo.SaveAsync(ct);
        return Ok200(MapTask(task));
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static object MapDefinition(WorkflowDefinition d) => new
    {
        d.Id, d.Name, d.Description, d.Category, d.Status, d.TriggerType,
        d.PublishedVersionId, d.CreatedAt, d.UpdatedAt
    };

    private static object MapInstance(WorkflowInstance i) => new
    {
        i.Id, i.DefinitionId, i.Status, i.TriggerType, i.StartedAt, i.CompletedAt,
        i.CurrentNodeId, i.SlaDeadline, i.CreatedAt
    };

    private static object MapTask(WorkflowTask t) => new
    {
        t.Id, t.InstanceId, t.NodeId, t.Title, t.Description, t.Status, t.AssigneeId,
        t.AssigneeType, t.Priority, t.DueAt, t.CompletedAt, t.Decision, t.Comment, t.CreatedAt
    };
}
