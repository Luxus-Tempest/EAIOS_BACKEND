using System.Security.Claims;
using EAIOS.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EAIOS.Api.Middleware;

public sealed class CurrentTenant
{
    public Guid? Id { get; internal set; }
}

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.CreateVersion7().ToString();
        context.TraceIdentifier = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        await next(context);
    }
}

public sealed class BearerTokenMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, TokenService tokens, InMemoryEaiosStore store)
    {
        var value = context.Request.Headers.Authorization.FirstOrDefault();
        if (value?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var payload = tokens.Validate(value[7..]);
            if (payload is not null && store.Sessions.TryGetValue(payload.SessionId, out var session) && !session.IsRevoked && session.ExpiresAt > DateTimeOffset.UtcNow)
            {
                var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, payload.UserId.ToString()), new("tenant_id", payload.OrganizationId.ToString()) };
                claims.AddRange(payload.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
            }
        }
        await next(context);
    }
}

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, CurrentTenant tenant)
    {
        var claimTenant = context.User.FindFirstValue("tenant_id");
        var headerTenant = context.Request.Headers["X-Tenant-ID"].FirstOrDefault();
        if (Guid.TryParse(claimTenant ?? headerTenant, out var id)) tenant.Id = id;
        await next(context);
    }
}

/// <summary>Enforces tenant isolation for every versioned endpoint except public auth flows.</summary>
public sealed class RequireTenantFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var path = context.HttpContext.Request.Path;
        if (!path.StartsWithSegments("/v1") || path.StartsWithSegments("/v1/auth")) return Task.CompletedTask;
        var tenant = context.HttpContext.RequestServices.GetRequiredService<CurrentTenant>();
        if (tenant.Id is null)
            context.Result = new ObjectResult(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Tenant context is required", Detail = "Supply a valid bearer token or X-Tenant-ID header." }) { StatusCode = StatusCodes.Status400BadRequest };
        return Task.CompletedTask;
    }
}

public static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
    public static bool IsOrganizationAdmin(this ClaimsPrincipal principal) => principal.IsInRole("org.admin");
}
