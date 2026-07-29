using EAIOS.Api.Domain.Shared.Interfaces;

namespace EAIOS.Api.Domain.Shared.Events;

/// <summary>Base record for all domain events.</summary>
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE IDENTITY
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record UserRegisteredEvent(Guid UserId, Guid OrganizationId, string Email)
    : DomainEventBase
{
    public override string EventType => "identity.user_registered";
}

public sealed record UserActivatedEvent(Guid UserId, Guid OrganizationId)
    : DomainEventBase
{
    public override string EventType => "identity.user_activated";
}

public sealed record UserSuspendedEvent(Guid UserId, Guid OrganizationId, string Reason)
    : DomainEventBase
{
    public override string EventType => "identity.user_suspended";
}

public sealed record UserPasswordResetRequestedEvent(Guid UserId, Guid OrganizationId, string Email)
    : DomainEventBase
{
    public override string EventType => "identity.password_reset_requested";
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE ORGANIZATION
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record TenantProvisionedEvent(Guid OrganizationId, string SchemaName)
    : DomainEventBase
{
    public override string EventType => "organization.tenant_provisioned";
}

public sealed record WorkspaceCreatedEvent(Guid WorkspaceId, Guid OrganizationId)
    : DomainEventBase
{
    public override string EventType => "organization.workspace_created";
}

public sealed record InvitationCreatedEvent(Guid InvitationId, Guid OrganizationId, string Email)
    : DomainEventBase
{
    public override string EventType => "organization.invitation_created";
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE RESOURCE
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record DocumentUploadedEvent(
    Guid DocumentId, Guid OrganizationId, Guid VersionId,
    string MimeType, long FileSizeBytes, string StorageKey)
    : DomainEventBase
{
    public override string EventType => "resource.document_uploaded";
}

public sealed record DocumentIndexingRequestedEvent(
    Guid DocumentId, Guid OrganizationId, Guid VersionId,
    string MimeType, bool OcrRequired)
    : DomainEventBase
{
    public override string EventType => "resource.document_indexing_requested";
}

public sealed record DocumentParsedEvent(
    Guid DocumentId, Guid OrganizationId, Guid VersionId,
    string ExtractedText, int PageCount)
    : DomainEventBase
{
    public override string EventType => "resource.document_parsed";
}

public sealed record DocumentDeletedEvent(Guid DocumentId, Guid OrganizationId)
    : DomainEventBase
{
    public override string EventType => "resource.document_deleted";
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE KNOWLEDGE
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record KnowledgeExtractionRequestedEvent(
    Guid DocumentId, Guid OrganizationId, Guid VersionId)
    : DomainEventBase
{
    public override string EventType => "knowledge.extraction_requested";
}

public sealed record KnowledgeItemCreatedEvent(
    Guid KnowledgeItemId, Guid OrganizationId, Guid? SourceDocumentId)
    : DomainEventBase
{
    public override string EventType => "knowledge.item_created";
}

public sealed record EmbeddingGenerationRequestedEvent(
    Guid ChunkId, Guid OrganizationId, string Content, string EmbeddingModel)
    : DomainEventBase
{
    public override string EventType => "knowledge.embedding_generation_requested";
}

public sealed record KnowledgeGraphUpdatedEvent(
    Guid OrganizationId, string[] AddedNodeIds, string[] AddedEdgeIds)
    : DomainEventBase
{
    public override string EventType => "knowledge.graph_updated";
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE AGENT
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record AgentExecutionStartedEvent(
    Guid ExecutionId, Guid AgentId, Guid OrganizationId, Guid? UserId)
    : DomainEventBase
{
    public override string EventType => "agent.execution_started";
}

public sealed record AgentExecutionCompletedEvent(
    Guid ExecutionId, Guid AgentId, Guid OrganizationId,
    bool IsSuccess, int TotalTokens, decimal CostUsd)
    : DomainEventBase
{
    public override string EventType => "agent.execution_completed";
}

public sealed record AgentHumanInputRequiredEvent(
    Guid ExecutionId, Guid AgentId, Guid OrganizationId,
    string Prompt, string[] Options)
    : DomainEventBase
{
    public override string EventType => "agent.human_input_required";
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE WORKFLOW
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record WorkflowInstanceStartedEvent(
    Guid InstanceId, Guid DefinitionId, Guid OrganizationId)
    : DomainEventBase
{
    public override string EventType => "workflow.instance_started";
}

public sealed record WorkflowTaskAssignedEvent(
    Guid TaskId, Guid InstanceId, Guid OrganizationId,
    Guid? AssigneeId, string AssigneeType)
    : DomainEventBase
{
    public override string EventType => "workflow.task_assigned";
}

public sealed record WorkflowTaskCompletedEvent(
    Guid TaskId, Guid InstanceId, Guid OrganizationId,
    Guid CompletedBy, string Decision)
    : DomainEventBase
{
    public override string EventType => "workflow.task_completed";
}

public sealed record WorkflowInstanceCompletedEvent(
    Guid InstanceId, Guid DefinitionId, Guid OrganizationId, bool IsSuccess)
    : DomainEventBase
{
    public override string EventType => "workflow.instance_completed";
}

public sealed record WorkflowSlaBreachedEvent(
    Guid InstanceId, Guid OrganizationId, DateTime DueAt, string SlaDefinition)
    : DomainEventBase
{
    public override string EventType => "workflow.sla_breached";
}

// ═══════════════════════════════════════════════════════════════════════════════
// MODULE NOTIFICATION
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record NotificationCreatedEvent(
    Guid NotificationId, Guid RecipientId, Guid OrganizationId, string Channel)
    : DomainEventBase
{
    public override string EventType => "notification.created";
}
