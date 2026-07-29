using EAIOS.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[ApiController]
public abstract class V1ApiController(CurrentTenant tenant) : ControllerBase
{
    protected Guid TenantId => tenant.Id ?? throw new InvalidOperationException("Tenant context is required.");
    protected IActionResult AuthenticationRequired() => Unauthorized(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Authentication required", Instance = HttpContext.Request.Path });
    protected IActionResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Insufficient permissions", Instance = HttpContext.Request.Path });
    protected bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
    protected bool IsOrganizationAdmin => User.IsOrganizationAdmin();
}
