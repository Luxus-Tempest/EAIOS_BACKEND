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
    IWorkflowService              workflowService,
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
        var def = await workflowService.CreateDefinitionAsync(TenantId, req.Name, req.Description, req.Category, req.NodesJson, ActorId.Value, ct);
        return Created201("GetWorkflowDefinition", new { id = def.Id }, MapDefinition(def));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDefinition(Guid id, [FromBody] UpdateWorkflowDefinitionRequest req, CancellationToken ct)
    {
        try
        {
            var def = await workflowService.UpdateDefinitionAsync(id, req.Name, req.Description, req.Category, req.NodesJson, ct);
            return Ok200(MapDefinition(def));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var def = await workflowService.PublishDefinitionAsync(id, ActorId.Value, ct);
            return Ok200(MapDefinition(def));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDefinition(Guid id, CancellationToken ct)
    {
        try
        {
            await workflowService.DeleteDefinitionAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
        try
        {
            var instance = await workflowService.StartInstanceAsync(TenantId, id, req.TriggerType, ActorId.Value, req.Variables, req.DueAt, ct);
            return Ok200(MapInstance(instance));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
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
        try
        {
            var instance = await workflowService.CancelInstanceAsync(instanceId, ct);
            return Ok200(MapInstance(instance));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
        try
        {
            var task = await workflowService.CompleteTaskAsync(taskId, ActorId.Value, req.Decision, req.Comment, req.FormData, ct);
            return Ok200(MapTask(task));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbidden(ex.Message);
        }
    }

    [HttpPost("tasks/{taskId:guid}/reassign")]
    public async Task<IActionResult> ReassignTask(Guid taskId, [FromBody] ReassignTaskRequest req, CancellationToken ct)
    {
        try
        {
            var task = await workflowService.ReassignTaskAsync(taskId, req.NewAssigneeId, ct);
            return Ok200(MapTask(task));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
