using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Workflow;

// ═══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ═══════════════════════════════════════════════════════════════════════════════

public enum WorkflowDefinitionStatus { Draft, Published, Deprecated, Archived }
public enum WorkflowTriggerType { Manual, Scheduled, Event, Api, Agent }
public enum WorkflowInstanceStatus { Initialized, Executing, WaitingForApproval, Paused, Completed, Cancelled, Failed, TimedOut }
public enum WorkflowTaskStatus { Open, InProgress, Completed, Cancelled, Escalated, Expired }
public enum WorkflowTaskAssigneeType { User, Department, Role, Workspace, Agent }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: WorkflowDefinition
// Table: org_{id}.workflow.definitions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class WorkflowDefinition : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Category { get; private set; } = "General";
    public WorkflowDefinitionStatus Status { get; private set; }
    public new string Version { get; private set; } = "1.0.0";
    public int VersionNumber { get; private set; } = 1;
    public Guid? PublishedVersionId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public string? IconCode { get; private set; }
    public string? Color { get; private set; }
    public bool IsTemplate { get; private set; }
    public string[] Tags { get; private set; } = [];
    public int ExecutionCount { get; private set; }
    public string? GraphJson { get; private set; }  // Draft graph (nodes + edges)

    // ── Relations ──────────────────────────────────────────────────────────────
    public IReadOnlyList<WorkflowDefinitionVersion> DefinitionVersions { get; private set; } = [];
    public IReadOnlyList<WorkflowInstance> Instances { get; private set; } = [];

    public static WorkflowDefinition Create(Guid organizationId, string name, Guid ownerId,
        string? description = null, string? graphJson = null)
    {
        var wfd = new WorkflowDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            Status = WorkflowDefinitionStatus.Draft,
            OwnerId = ownerId,
            GraphJson = graphJson
        };
        wfd.SetOrganizationId(organizationId);
        wfd.SetCreated(ownerId);
        return wfd;
    }

    public void Publish(Guid versionId, string versionLabel)
    {
        Status = WorkflowDefinitionStatus.Published;
        PublishedVersionId = versionId;
        Version = versionLabel;
        VersionNumber++;
    }

    public void Deprecate() => Status = WorkflowDefinitionStatus.Deprecated;

    public void Update(string? name, string? description, string? graphJson, string? category)
    {
        if (Status == WorkflowDefinitionStatus.Published)
            throw new InvalidOperationException("Cannot modify a published workflow definition.");
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (description is not null) Description = description;
        if (graphJson is not null) GraphJson = graphJson;
        if (!string.IsNullOrWhiteSpace(category)) Category = category;
    }

    public void IncrementExecutionCount() => ExecutionCount++;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: WorkflowDefinitionVersion (Immutable after publication)
// Table: org_{id}.workflow.definition_versions
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class WorkflowDefinitionVersion : TenantEntity
{
    public Guid DefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public string VersionLabel { get; private set; } = string.Empty;
    public string GraphJson { get; private set; } = "{}";
    public string? ChangeLog { get; private set; }
    public Guid PublishedBy { get; private set; }
    public DateTime PublishedAt { get; private set; }

    public static WorkflowDefinitionVersion Create(Guid organizationId, Guid definitionId,
        int versionNumber, string versionLabel, string graphJson, Guid publishedBy, string? changeLog = null)
    {
        var v = new WorkflowDefinitionVersion
        {
            Id = Guid.CreateVersion7(),
            DefinitionId = definitionId,
            VersionNumber = versionNumber,
            VersionLabel = versionLabel,
            GraphJson = graphJson,
            ChangeLog = changeLog,
            PublishedBy = publishedBy,
            PublishedAt = DateTime.UtcNow
        };
        v.SetOrganizationId(organizationId);
        v.SetCreated(publishedBy);
        return v;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: WorkflowInstance
// Table: org_{id}.workflow.instances
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class WorkflowInstance : TenantEntity
{
    public Guid DefinitionId { get; private set; }
    public Guid DefinitionVersionId { get; private set; }
    public string DefinitionVersion { get; private set; } = string.Empty;
    public WorkflowTriggerType TriggerType { get; private set; }
    public Guid? TriggeredBy { get; private set; }
    public string? TriggerContextJson { get; private set; }
    public WorkflowInstanceStatus Status { get; private set; }
    public string? CurrentStepId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? DueAt { get; private set; }
    public int RetryCount { get; private set; }
    public string VariablesJson { get; private set; } = "{}";
    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<WorkflowTask> Tasks { get; private set; } = [];

    public static WorkflowInstance Create(Guid organizationId, Guid definitionId, Guid definitionVersionId,
        string definitionVersion, WorkflowTriggerType triggerType, Guid? triggeredBy,
        string variablesJson = "{}", DateTime? dueAt = null)
    {
        var inst = new WorkflowInstance
        {
            Id = Guid.CreateVersion7(),
            DefinitionId = definitionId,
            DefinitionVersionId = definitionVersionId,
            DefinitionVersion = definitionVersion,
            TriggerType = triggerType,
            TriggeredBy = triggeredBy,
            Status = WorkflowInstanceStatus.Initialized,
            StartedAt = DateTime.UtcNow,
            VariablesJson = variablesJson,
            DueAt = dueAt
        };
        inst.SetOrganizationId(organizationId);
        inst.SetCreated(triggeredBy);
        return inst;
    }

    public void Start(string? firstStepId) { Status = WorkflowInstanceStatus.Executing; CurrentStepId = firstStepId; }
    public void WaitForApproval(string stepId) { Status = WorkflowInstanceStatus.WaitingForApproval; CurrentStepId = stepId; }
    public void Resume(string? nextStepId) { Status = WorkflowInstanceStatus.Executing; CurrentStepId = nextStepId; }
    public void Pause() => Status = WorkflowInstanceStatus.Paused;
    public void Complete() { Status = WorkflowInstanceStatus.Completed; CompletedAt = DateTime.UtcNow; }
    public void Fail(string error) { Status = WorkflowInstanceStatus.Failed; CompletedAt = DateTime.UtcNow; ErrorMessage = error; }
    public void Cancel() { Status = WorkflowInstanceStatus.Cancelled; CompletedAt = DateTime.UtcNow; }
    public void UpdateVariables(string variablesJson) => VariablesJson = variablesJson;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: WorkflowTask (Human-in-the-Loop)
// Table: org_{id}.workflow.tasks
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class WorkflowTask : TenantEntity
{
    public Guid InstanceId { get; private set; }
    public string StepId { get; private set; } = string.Empty;
    public string TaskType { get; private set; } = string.Empty;  // Approval, Review, DataInput
    public string Title { get; private set; } = string.Empty;
    public string? Instructions { get; private set; }
    public WorkflowTaskAssigneeType AssigneeType { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public Guid? AssignedGroupId { get; private set; }
    public WorkflowTaskStatus Status { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime? EscalatedAt { get; private set; }
    public Guid? EscalatedTo { get; private set; }
    public int EscalationLevel { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? Decision { get; private set; }  // approved/rejected/custom
    public string? Comment { get; private set; }
    public string? FormDataJson { get; private set; }

    public static WorkflowTask Create(Guid organizationId, Guid instanceId, string stepId,
        string taskType, string title, WorkflowTaskAssigneeType assigneeType,
        Guid? assigneeId, DateTime? dueAt = null)
    {
        var task = new WorkflowTask
        {
            Id = Guid.CreateVersion7(),
            InstanceId = instanceId,
            StepId = stepId,
            TaskType = taskType,
            Title = title,
            AssigneeType = assigneeType,
            AssigneeId = assigneeId,
            Status = WorkflowTaskStatus.Open,
            DueAt = dueAt
        };
        task.SetOrganizationId(organizationId);
        task.SetCreated(null);
        return task;
    }

    public void Complete(Guid completedBy, string decision, string? comment, string? formDataJson)
    {
        Status = WorkflowTaskStatus.Completed;
        CompletedBy = completedBy;
        CompletedAt = DateTime.UtcNow;
        Decision = decision;
        Comment = comment;
        FormDataJson = formDataJson;
    }

    public void Reassign(Guid newAssigneeId, WorkflowTaskAssigneeType assigneeType)
    {
        AssigneeId = newAssigneeId;
        AssigneeType = assigneeType;
        Status = WorkflowTaskStatus.Open;
    }

    public void Escalate(Guid escalatedTo, int level)
    {
        EscalatedAt = DateTime.UtcNow;
        EscalatedTo = escalatedTo;
        EscalationLevel = level;
        Status = WorkflowTaskStatus.Escalated;
    }

    public void Cancel() { Status = WorkflowTaskStatus.Cancelled; }
}
