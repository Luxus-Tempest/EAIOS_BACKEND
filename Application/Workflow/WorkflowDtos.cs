using EAIOS.Api.Domain.Workflow;

namespace EAIOS.Api.Application.Workflow;

public sealed record CreateWorkflowDefinitionRequest(
    string              Name,
    string?             Description = null,
    string              Category    = "General",
    string?             NodesJson   = null,
    string?             EdgesJson   = null,
    WorkflowTriggerType TriggerType = WorkflowTriggerType.Manual,
    string?             TriggerConfig = null);

public sealed record UpdateWorkflowDefinitionRequest(
    string?  Name        = null,
    string?  Description = null,
    string?  Category    = null,
    string?  NodesJson   = null,
    string?  EdgesJson   = null);

public sealed record StartWorkflowRequest(
    string? InputDataJson = null,
    string? ContextJson   = null);

public sealed record CancelWorkflowRequest(string? Reason = null);

public sealed record CompleteTaskRequest(
    string  Decision,
    string? Comment        = null,
    string? OutputDataJson = null);

public sealed record ReassignTaskRequest(
    Guid    NewAssigneeId,
    string? Reason = null);
