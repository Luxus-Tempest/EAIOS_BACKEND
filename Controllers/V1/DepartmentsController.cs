using EAIOS.Api.Application.Organization;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Départements organisationnels : CRUD hiérarchique + membres.
/// Route : /api/v1/departments
/// </summary>
[Route("api/v1/departments")]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class DepartmentsController(
    IDepartmentService departmentService,
    IDepartmentRepository departmentRepo,
    IMembershipRepository membershipRepo) : V1ApiController
{
    // ── GET /api/v1/departments ───────────────────────────────────────────────
    /// <summary>Retourne tous les départements de l'organisation courante.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var all = await departmentRepo.GetAllAsync(ct);
        return Ok200(all.Select(MapDept).ToList());
    }

    // ── GET /api/v1/departments/{id} ──────────────────────────────────────────
    [HttpGet("{id:guid}", Name = "GetDepartment")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        return dept == null ? NotFound() : Ok200(MapDept(dept));
    }

    // ── GET /api/v1/departments/{id}/children ─────────────────────────────────
    [HttpGet("{id:guid}/children")]
    public async Task<IActionResult> GetChildren(Guid id, CancellationToken ct)
    {
        var children = await departmentRepo.GetChildrenAsync(id, ct);
        return Ok200(children.Select(MapDept).ToList());
    }

    // ── POST /api/v1/departments ──────────────────────────────────────────────
    [HttpPost]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "department.manage")]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        
        try
        {
            var dept = await departmentService.CreateDepartmentAsync(TenantId, req.Name, ActorId.Value, req.ParentId, req.Description, ct);
            return Created201("GetDepartment", new { id = dept.Id }, MapDept(dept));
        }
        catch (InvalidOperationException ex) when (ex.Message == "NAME_ALREADY_EXISTS")
        {
            return Conflict(new { code = "NAME_ALREADY_EXISTS", message = "Un département porte déjà ce nom à ce niveau." });
        }
    }

    // ── PUT /api/v1/departments/{id} ──────────────────────────────────────────
    [HttpPut("{id:guid}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "department.manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest req, CancellationToken ct)
    {
        try
        {
            var dept = await departmentService.UpdateDepartmentAsync(id, req.Name, req.Description, req.ManagerId, req.Code, ct);
            return Ok200(MapDept(dept));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "NAME_ALREADY_EXISTS")
        {
            return Conflict(new { code = "NAME_ALREADY_EXISTS", message = "Un département porte déjà ce nom à ce niveau." });
        }
    }

    // ── DELETE /api/v1/departments/{id} ───────────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "department.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await departmentService.DeleteDepartmentAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "HAS_CHILDREN")
        {
            return UnprocessableEntity("Supprimez d'abord les sous-départements avant de supprimer ce département.");
        }
    }

    // ── GET /api/v1/departments/{id}/members ──────────────────────────────────
    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        if (dept == null) return NotFound();

        var members = await membershipRepo.GetByDepartmentAsync(id, ct);
        return Ok200(members.Select(m => new
        {
            m.Id,
            m.UserId,
            Role = m.Type,
            m.Status,
            m.JoinedAt,
            m.CreatedAt
        }).ToList());
    }

    // ── POST /api/v1/departments/{id}/members ─────────────────────────────────
    [HttpPost("{id:guid}/members")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "department.manage")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var membership = await departmentService.AddMemberAsync(TenantId, id, req.UserId, ct);
            return Ok200(new
            {
                membership.Id,
                membership.UserId,
                Role = membership.Type,
                membership.Status,
                membership.JoinedAt
            });
        }
        catch (InvalidOperationException ex) when (ex.Message == "ALREADY_MEMBER")
        {
            return Conflict("Cet utilisateur est déjà membre de ce département.");
        }
    }

    // ── DELETE /api/v1/departments/{id}/members/{userId} ──────────────────────
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "department.manage")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        try
        {
            await departmentService.RemoveMemberAsync(id, userId, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static EAIOS.Api.Application.Organization.DepartmentDto MapDept(Department d) => new EAIOS.Api.Application.Organization.DepartmentDto(
        d.Id,
        d.Name,
        d.Description,
        d.Code,
        d.Status,
        d.ParentId,
        d.ManagerId,
        d.Color,
        d.IconCode,
        d.MemberCount,
        d.CreatedAt
    );
}
