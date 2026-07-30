using EAIOS.Api.Application.Common.Interfaces;
using System.Security.Claims;

namespace EAIOS.Api.Middleware;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var val = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("sub")?.Value;
            return Guid.TryParse(val, out var id) ? id : null;
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            var val = User?.FindFirst("org_id")?.Value;
            return Guid.TryParse(val, out var id) ? id : null;
        }
    }

    public string? Email => User?.FindFirst(ClaimTypes.Email)?.Value ?? User?.FindFirst("email")?.Value;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public bool IsPlatformAdmin => HasRole("platform.admin") || HasRole("platform.owner");
    public bool IsOrganizationAdmin => HasRole("org.admin");

    public IReadOnlyList<string> Roles => User?.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList()
                                         ?? (IReadOnlyList<string>)Array.Empty<string>();

    public IReadOnlyList<string> Permissions => User?.FindAll("permission").Select(c => c.Value).Distinct().ToList()
                                               ?? (IReadOnlyList<string>)Array.Empty<string>();

    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
