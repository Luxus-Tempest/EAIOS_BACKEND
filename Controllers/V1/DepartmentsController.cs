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
public sealed class DepartmentsController(
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
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var dept = Department.Create(
            TenantId, req.Name, ActorId.Value, req.ParentId, req.Description);

        await departmentRepo.AddAsync(dept, ct);
        await departmentRepo.SaveAsync(ct);

        return Created201("GetDepartment", new { id = dept.Id }, MapDept(dept));
    }

    // ── PUT /api/v1/departments/{id} ──────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest req, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        if (dept == null) return NotFound();

        dept.Update(req.Name, req.Description, req.ManagerId, req.Code);
        departmentRepo.Update(dept);
        await departmentRepo.SaveAsync(ct);

        return Ok200(MapDept(dept));
    }

    // ── DELETE /api/v1/departments/{id} ───────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        if (dept == null) return NotFound();

        var children = await departmentRepo.GetChildrenAsync(id, ct);
        if (children.Count > 0)
            return UnprocessableEntity("Supprimez d'abord les sous-départements avant de supprimer ce département.");

        departmentRepo.SoftDelete(dept);
        await departmentRepo.SaveAsync(ct);
        return NoContent204();
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
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var dept = await departmentRepo.GetByIdAsync(id, ct);
        if (dept == null) return NotFound();

        var existing = await membershipRepo.FindAsync(req.UserId, null, id, ct);
        if (existing != null)
            return Conflict("Cet utilisateur est déjà membre de ce département.");

        var membership = Membership.Create(TenantId, req.UserId,
            MembershipType.Member, departmentId: id);

        await membershipRepo.AddAsync(membership, ct);
        await membershipRepo.SaveAsync(ct);

        return Ok200(new
        {
            membership.Id,
            membership.UserId,
            Role = membership.Type,
            membership.Status,
            membership.JoinedAt
        });
    }

    // ── DELETE /api/v1/departments/{id}/members/{userId} ──────────────────────
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var membership = await membershipRepo.FindAsync(userId, null, id, ct);
        if (membership == null) return NotFound();

        membershipRepo.SoftDelete(membership);
        await membershipRepo.SaveAsync(ct);
        return NoContent204();
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
