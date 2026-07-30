using EAIOS.Api.Application.Organization;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Espaces de travail (Workspaces).
/// </summary>
[Route("api/v1/workspaces")]
public sealed class WorkspacesController(
    IWorkspaceRepository  workspaceRepo,
    IMembershipRepository membershipRepo) : V1ApiController
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await workspaceRepo.GetPagedAsync(page, pageSize, ct);
        return OkList(result.Items.Select(MapWs).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("{id:guid}", Name = "GetWorkspace")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        return ws == null ? NotFound() : Ok200(MapWs(ws));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var ws = Workspace.Create(TenantId, req.Name, req.Description, req.Visibility, ActorId.Value);
        await workspaceRepo.AddAsync(ws, ct);
        await workspaceRepo.SaveAsync(ct);
        return Created201("GetWorkspace", new { id = ws.Id }, MapWs(ws));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkspaceRequest req, CancellationToken ct)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        if (ws == null) return NotFound();
        ws.Update(req.Name, req.Description, req.AvatarUrl, req.Visibility);
        workspaceRepo.Update(ws);
        await workspaceRepo.SaveAsync(ct);
        return Ok200(MapWs(ws));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        if (ws == null) return NotFound();
        workspaceRepo.SoftDelete(ws);
        await workspaceRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Membres ───────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var members = await membershipRepo.GetByWorkspaceAsync(id, ct);
        return Ok200(members.Select(m => new { m.Id, m.UserId, m.Role, m.Status, m.JoinedAt }).ToList());
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var existing = await membershipRepo.FindAsync(req.UserId, id, null, ct);
        if (existing != null)
            return Conflict("Cet utilisateur est déjà membre de cet espace.");

        var membership = Membership.Create(TenantId, req.UserId, workspaceId: id, role: req.Role, invitedBy: ActorId.Value);
        await membershipRepo.AddAsync(membership, ct);
        await membershipRepo.SaveAsync(ct);
        return Ok200(new { membership.Id, membership.UserId, membership.Role, membership.Status, membership.JoinedAt });
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var membership = await membershipRepo.FindAsync(userId, id, null, ct);
        if (membership == null) return NotFound();
        membershipRepo.SoftDelete(membership);
        await membershipRepo.SaveAsync(ct);
        return NoContent204();
    }

    private static object MapWs(Workspace w) => new
    {
        w.Id, w.Name, w.Slug, w.Description, w.AvatarUrl, w.Status, w.Visibility, w.CreatedAt
    };
}

/// <summary>
/// Départements.
/// </summary>
[Route("api/v1/departments")]
public sealed class DepartmentsController(
    IDepartmentRepository departmentRepo,
    IMembershipRepository membershipRepo) : V1ApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var all = await departmentRepo.GetAllAsync(ct);
        return Ok200(all.Select(MapDept).ToList());
    }

    [HttpGet("{id:guid}", Name = "GetDepartment")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        return dept == null ? NotFound() : Ok200(MapDept(dept));
    }

    [HttpGet("{id:guid}/children")]
    public async Task<IActionResult> GetChildren(Guid id, CancellationToken ct)
    {
        var children = await departmentRepo.GetChildrenAsync(id, ct);
        return Ok200(children.Select(MapDept).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var dept = Department.Create(TenantId, req.Name, req.Code, req.ParentId, ActorId.Value);
        await departmentRepo.AddAsync(dept, ct);
        await departmentRepo.SaveAsync(ct);
        return Created201("GetDepartment", new { id = dept.Id }, MapDept(dept));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest req, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        if (dept == null) return NotFound();
        dept.Update(req.Name, req.Code, req.Description, req.ManagerId);
        departmentRepo.Update(dept);
        await departmentRepo.SaveAsync(ct);
        return Ok200(MapDept(dept));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var dept = await departmentRepo.GetByIdAsync(id, ct);
        if (dept == null) return NotFound();
        departmentRepo.SoftDelete(dept);
        await departmentRepo.SaveAsync(ct);
        return NoContent204();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var members = await membershipRepo.GetByDepartmentAsync(id, ct);
        return Ok200(members.Select(m => new { m.Id, m.UserId, m.Role, m.Status, m.JoinedAt }).ToList());
    }

    private static object MapDept(Department d) => new
    {
        d.Id, d.Name, d.Code, d.ParentId, d.ManagerId, d.Status, d.Depth, d.CreatedAt
    };
}
