namespace EAIOS.Api.Application.Common.Interfaces;

/// <summary>Provides current authenticated user context to application/domain services.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    bool IsOrganizationAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasRole(string role);
    bool HasPermission(string permission);
}

/// <summary>Current tenant context — isolates all DB queries by OrganizationId.</summary>
public interface ITenantContext
{
    Guid OrganizationId { get; }
    string TenantSchemaName { get; }
    bool IsResolved { get; }
    void SetTenant(Guid organizationId);
}
