namespace EAIOS.Api.Domain.Shared.Interfaces;

/// <summary>Multi-tenant isolation contract. All entities must have an OrganizationId.</summary>
public interface ITenantScoped
{
    Guid OrganizationId { get; }
}

/// <summary>Soft-delete contract. Records are never physically deleted.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTime? DeletedAt { get; }
    Guid? DeletedBy { get; }
}

/// <summary>Full audit trail: creation and modification tracking.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; }
    Guid? CreatedBy { get; }
    DateTime UpdatedAt { get; }
    Guid? UpdatedBy { get; }
}

/// <summary>Marker interface for all domain events.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
    string EventType { get; }
}
