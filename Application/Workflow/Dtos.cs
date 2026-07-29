using EAIOS.Api.Domain.Workflow;

namespace EAIOS.Api.Application.Workflow;

// ── WorkflowDefinition ────────────────────────────────────────────────────────

public sealed record WorkflowDefinitionDto(
    Guid Id,
    string Name,
    string? Description,
    string Category,
    WorkflowDefinitionStatus Status,
    string Version,
    int VersionNumber,
    Guid OwnerId,
    bool IsTemplate,
    string[] Tags,
    int ExecutionCount,
    DateTime CreatedAt);

public sealed record CreateWorkflowRequest(
    string Name,
    string? Description = null,
    string Category = "General",
    string? GraphJson = null,
    string[]? Tags = null,
    bool IsTemplate = false);

public sealed record UpdateWorkflowRequest(
    string? Name,
    string? Description,
    string? GraphJson,
    string? Category);

public sealed record WorkflowDefinitionVersionDto(
    Guid Id,
    int VersionNumber,
    string VersionLabel,
    string? ChangeLog,
    Guid PublishedBy,
    DateTime PublishedAt);

// ── WorkflowInstance ──────────────────────────────────────────────────────────

public sealed record WorkflowInstanceDto(
    Guid Id,
    Guid DefinitionId,
    string DefinitionVersion,
    WorkflowTriggerType TriggerType,
    Guid? TriggeredBy,
    WorkflowInstanceStatus Status,
    string? CurrentStepId,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime? DueAt,
    Dictionary<string, object>? Variables,
    string? ErrorMessage,
    IReadOnlyList<WorkflowTaskSummaryDto> Tasks);

public sealed record StartWorkflowRequest(
    Guid DefinitionId,
    WorkflowTriggerType TriggerType = WorkflowTriggerType.Manual,
    Dictionary<string, object>? Variables = null,
    DateTime? DueAt = null);

public sealed record WorkflowTaskSummaryDto(
    Guid Id,
    string Title,
    WorkflowTaskStatus Status,
    Guid? AssigneeId,
    DateTime? DueAt,
    string? Decision);

// ── WorkflowTask ──────────────────────────────────────────────────────────────

public sealed record WorkflowTaskDto(
    Guid Id,
    Guid InstanceId,
    string StepId,
    string TaskType,
    string Title,
    string? Instructions,
    WorkflowTaskAssigneeType AssigneeType,
    Guid? AssigneeId,
    WorkflowTaskStatus Status,
    DateTime? DueAt,
    int EscalationLevel,
    Guid? CompletedBy,
    DateTime? CompletedAt,
    string? Decision,
    string? Comment,
    DateTime CreatedAt);

public sealed record CompleteTaskRequest(
    string Decision,
    string? Comment = null,
    Dictionary<string, object>? FormData = null);

public sealed record ReassignTaskRequest(Guid NewAssigneeId, WorkflowTaskAssigneeType AssigneeType = WorkflowTaskAssigneeType.User);
