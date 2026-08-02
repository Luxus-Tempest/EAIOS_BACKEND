using EAIOS.Api.Domain.Shared.Interfaces;

namespace EAIOS.Api.Domain.Shared.Primitives;

/// <summary>
/// Mandatory base class for ALL tenant-scoped business entities.
/// Provides: OrganizationId isolation, soft-delete, full audit trail, domain event dispatch.
/// </summary>
public abstract class TenantEntity : Entity<Guid>, ITenantScoped, ISoftDeletable, IAuditable
{
    // ── Multi-Tenancy ──────────────────────────────────────────────────────────
    public Guid OrganizationId { get; private set; }

    // ── Soft Delete ────────────────────────────────────────────────────────────
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    // ── Audit ──────────────────────────────────────────────────────────────────
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    // ── Concurrency ────────────────────────────────────────────────────────────
    public uint Version { get; private set; }

    // ── Domain Events ──────────────────────────────────────────────────────────
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected TenantEntity() { }

    // ── Internal Mutators (called by EF Core / DbContext) ─────────────────────
    internal void SetOrganizationId(Guid organizationId) => OrganizationId = organizationId;

    internal void SetCreated(Guid? createdBy)
    {
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        // Assign UUID v7 if not already set
        if (Id == Guid.Empty) Id = Guid.CreateVersion7();
    }

    internal void SetUpdated(Guid? updatedBy)
    {
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    internal void SetSoftDeleted(Guid? deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        SetUpdated(deletedBy);
    }

    protected void RaiseDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
