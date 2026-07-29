using EAIOS.Api.Contracts;
using EAIOS.Api.Domain;
using EAIOS.Api.Infrastructure;
using EAIOS.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[Route("v1/departments")]
public sealed class DepartmentsController(InMemoryEaiosStore store, CurrentTenant tenant) : V1ApiController(tenant)
{
    [HttpGet]
    public IActionResult List() => !IsAuthenticated ? AuthenticationRequired() : Ok(new { data = store.Departments.Values.Where(d => d.OrganizationId == TenantId && !d.IsDeleted).OrderBy(d => d.Name) });

    [HttpPost]
    public IActionResult Create(CreateDepartmentRequest request)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!IsOrganizationAdmin) return Forbidden();
        if (store.Departments.Values.Any(d => d.OrganizationId == TenantId && d.Code.Equals(request.Code, StringComparison.OrdinalIgnoreCase) && !d.IsDeleted)) return Conflict();
        var department = new Department { OrganizationId = TenantId, Name = request.Name.Trim(), Code = request.Code.Trim(), WorkspaceId = request.WorkspaceId, ManagerId = request.ManagerId };
        store.Departments[department.Id] = department;
        return Created($"/v1/departments/{department.Id}", new { data = department });
    }

    [HttpGet("{departmentId:guid}")]
    public IActionResult Get(Guid departmentId) => !IsAuthenticated ? AuthenticationRequired() : TryGet(departmentId, out var department) ? Ok(new { data = department }) : NotFound();

    [HttpPut("{departmentId:guid}")]
    public IActionResult Update(Guid departmentId, UpdateDepartmentRequest request)
    {
        if (!IsAuthenticated) return AuthenticationRequired(); if (!IsOrganizationAdmin) return Forbidden(); if (!TryGet(departmentId, out var department)) return NotFound();
        department.Name = request.Name.Trim(); department.Code = request.Code.Trim(); department.WorkspaceId = request.WorkspaceId; department.ManagerId = request.ManagerId;
        return Ok(new { data = department });
    }

    [HttpDelete("{departmentId:guid}")]
    public IActionResult Delete(Guid departmentId) { if (!IsAuthenticated) return AuthenticationRequired(); if (!IsOrganizationAdmin) return Forbidden(); if (!TryGet(departmentId, out var department)) return NotFound(); department.IsDeleted = true; return NoContent(); }

    [HttpGet("{departmentId:guid}/members")]
    public IActionResult Members(Guid departmentId) { if (!IsAuthenticated) return AuthenticationRequired(); if (!TryGet(departmentId, out var department)) return NotFound(); return Ok(new { data = store.Users.Values.Where(u => department.MemberIds.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }) }); }

    [HttpPost("{departmentId:guid}/members")]
    public IActionResult AddMember(Guid departmentId, AddMemberRequest request) { if (!IsAuthenticated) return AuthenticationRequired(); if (!IsOrganizationAdmin) return Forbidden(); if (!TryGet(departmentId, out var department)) return NotFound(); if (!store.Users.TryGetValue(request.UserId, out var user) || user.OrganizationId != TenantId) return NotFound(); department.MemberIds.Add(user.Id); return NoContent(); }

    private bool TryGet(Guid id, out Department department) => store.Departments.TryGetValue(id, out department!) && department.OrganizationId == TenantId && !department.IsDeleted;
}
