using System.Collections.Concurrent;
using EAIOS.Api.Domain;

namespace EAIOS.Api.Infrastructure;

/// <summary>Thread-safe development adapter. Replace with the EF Core/PostgreSQL adapter in production.</summary>
public sealed class InMemoryEaiosStore
{
    public ConcurrentDictionary<Guid, Organization> Organizations { get; } = new();
    public ConcurrentDictionary<Guid, User> Users { get; } = new();
    public ConcurrentDictionary<Guid, Session> Sessions { get; } = new();
    public ConcurrentDictionary<Guid, Workspace> Workspaces { get; } = new();
    public ConcurrentDictionary<Guid, Department> Departments { get; } = new();

    public User? FindUser(Guid organizationId, string email) => Users.Values.SingleOrDefault(u =>
        u.OrganizationId == organizationId && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
}
