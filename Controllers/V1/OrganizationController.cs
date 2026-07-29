using EAIOS.Api.Contracts;
using EAIOS.Api.Infrastructure;
using EAIOS.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[Route("v1/organization")]
public sealed class OrganizationController(InMemoryEaiosStore store, CurrentTenant tenant) : V1ApiController(tenant)
{
    [HttpGet]
    public IActionResult Get()
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        return store.Organizations.TryGetValue(TenantId, out var organization)
            ? Ok(new { data = organization })
            : NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Organization not found" });
    }

    [HttpPut]
    public IActionResult Update(UpdateOrganizationRequest request)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!IsOrganizationAdmin) return Forbidden();
        if (!store.Organizations.TryGetValue(TenantId, out var organization)) return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Name)) organization.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.DefaultLanguage)) organization.DefaultLanguage = request.DefaultLanguage;
        if (!string.IsNullOrWhiteSpace(request.TimeZone)) organization.TimeZone = request.TimeZone;
        return Ok(new { data = organization });
    }
}
