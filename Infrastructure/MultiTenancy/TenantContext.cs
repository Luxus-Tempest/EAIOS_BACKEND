using EAIOS.Api.Application.Common.Interfaces;

namespace EAIOS.Api.Infrastructure.MultiTenancy;

/// <summary>
/// Contexte de tenant scopé à la requête HTTP.
/// Peuplé par TenantResolutionMiddleware à partir du claim JWT ou du header HTTP.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid _organizationId = Guid.Empty;

    public Guid OrganizationId => _organizationId;

    public bool IsResolved => _organizationId != Guid.Empty;

    public string TenantSchemaName =>
        IsResolved ? $"org_{_organizationId:N}" : "public";

    public void SetTenant(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId cannot be empty.", nameof(organizationId));
        _organizationId = organizationId;
    }
}
