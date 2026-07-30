using EAIOS.Api.Application.Common.Interfaces;

namespace EAIOS.Api.Middleware;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        // 1. Try claim 'org_id' from JWT token
        if (currentUser.OrganizationId.HasValue && currentUser.OrganizationId != Guid.Empty)
        {
            tenantContext.SetTenant(currentUser.OrganizationId.Value);
        }
        // 2. Try header 'X-Organization-ID' or 'X-Tenant-ID'
        else if (context.Request.Headers.TryGetValue("X-Organization-ID", out var orgHeader) &&
                 Guid.TryParse(orgHeader, out var orgIdHeader))
        {
            tenantContext.SetTenant(orgIdHeader);
        }
        else if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantHeader) &&
                 Guid.TryParse(tenantHeader, out var tenantIdHeader))
        {
            tenantContext.SetTenant(tenantIdHeader);
        }

        await next(context);
    }
}
