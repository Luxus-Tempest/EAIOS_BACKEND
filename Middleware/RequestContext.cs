using EAIOS.Api.Application.Common.Interfaces;
using System.Security.Claims;

namespace EAIOS.Api.Middleware;

/// <summary>
/// Contexte de requête courant — implémente ICurrentUser depuis les claims JWT.
/// Scoped par requête HTTP.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var val = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(val, out var id) ? id : null;
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            var val = Principal?.FindFirstValue("org_id");
            return Guid.TryParse(val, out var id) ? id : null;
        }
    }

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email) ??
        Principal?.FindFirstValue("email");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsPlatformAdmin =>
        HasRole("platform.admin") || HasRole("platform.owner");

    public bool IsOrganizationAdmin =>
        HasRole("org.admin") || HasRole(Domain.AccessControl.SystemRoles.OrgAdmin);

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList()
        ?? (IReadOnlyList<string>)[];

    public IReadOnlyList<string> Permissions =>
        Principal?.FindAll("permission").Select(c => c.Value).Distinct().ToList()
        ?? (IReadOnlyList<string>)[];

    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Middleware de résolution du tenant depuis le claim JWT ou l'en-tête HTTP.
/// Doit être exécuté APRÈS l'authentification JWT.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        // Priorité 1 : claim org_id du JWT — la seule source pour un utilisateur ordinaire.
        if (currentUser.OrganizationId.HasValue && currentUser.OrganizationId != Guid.Empty)
        {
            tenantContext.SetTenant(currentUser.OrganizationId.Value);
        }
        // Priorité 2 : en-tête, réservé à un administrateur plateforme authentifié.
        // Un en-tête est contrôlé par le client : l'honorer pour n'importe quel
        // porteur de jeton lui permettrait de choisir son organisation.
        else if (currentUser.IsAuthenticated && currentUser.IsPlatformAdmin
                 && TryReadOrganizationHeader(context, out var orgId))
        {
            tenantContext.SetTenant(orgId);
        }

        await next(context);
    }

    private static bool TryReadOrganizationHeader(HttpContext context, out Guid organizationId)
    {
        organizationId = Guid.Empty;
        return (context.Request.Headers.TryGetValue("X-Organization-ID", out var orgHeader) && Guid.TryParse(orgHeader, out organizationId))
            || (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantHeader) && Guid.TryParse(tenantHeader, out organizationId));
    }
}

/// <summary>
/// Middleware Correlation ID : assign un ID unique à chaque requête et le propage en réponse.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            || string.IsNullOrWhiteSpace(incoming))
        {
            incoming = Guid.CreateVersion7().ToString("N");
        }

        var correlationId = incoming.ToString();
        context.Items[HeaderName]       = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }
}
