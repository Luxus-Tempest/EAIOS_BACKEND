namespace EAIOS.Api.Domain.Shared.Primitives;

/// <summary>
/// Base class for all domain entities with an identity.
/// </summary>
public abstract class Entity<TId>
{
    public TId Id { get; set; } = default!;
    public DateTime CreatedAt { get; protected set; }
    public DateTime UpdatedAt { get; protected set; }

    /// <summary>
    /// Horodate la modification. Les entités non tenant-scopées (Organization,
    /// FeatureFlag…) ne passent pas par l'intercepteur d'audit du DbContext tenant :
    /// leurs services appelants doivent donc marquer explicitement la mise à jour.
    /// </summary>
    public void Touch() => UpdatedAt = DateTime.UtcNow;
}
