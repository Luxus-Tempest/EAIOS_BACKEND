namespace EAIOS.Api.Domain.Shared.Primitives;

/// <summary>
/// Base class for all domain entities with an identity.
/// </summary>
public abstract class Entity<TId>
{
    public TId Id { get; set; } = default!;
    public DateTime CreatedAt { get; protected set; }
    public DateTime UpdatedAt { get; protected set; }
}
