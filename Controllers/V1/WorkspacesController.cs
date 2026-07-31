using EAIOS.Api.Application.Organization;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Organization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Espaces de travail (Workspaces) : CRUD + gestion des membres.
/// Route : /api/v1/workspaces
/// </summary>
[Route("api/v1/workspaces")]
public sealed class WorkspacesController(
    IWorkspaceRepository  workspaceRepo,
    IMembershipRepository membershipRepo) : V1ApiController
{
    // ── GET /api/v1/workspaces ────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await workspaceRepo.GetPagedAsync(page, pageSize, null, null, ct);
        return OkList(result.Items.Select(MapWs).ToList(), result.TotalCount, page, pageSize);
    }

    // ── GET /api/v1/workspaces/{id} ───────────────────────────────────────────
    [HttpGet("{id:guid}", Name = "GetWorkspace")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        return ws == null ? NotFound() : Ok200(MapWs(ws));
    }

    // ── POST /api/v1/workspaces ───────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name est requis.");

        var ws = Workspace.Create(TenantId, req.Name, ActorId.Value, req.Type, req.Description);
        if (req.Color != null || req.IconCode != null)
        {
            ws.Update(null, null, req.Color, req.IconCode);
        }
        await workspaceRepo.AddAsync(ws, ct);
        await workspaceRepo.SaveAsync(ct);

        return Created201("GetWorkspace", new { id = ws.Id }, MapWs(ws));
    }

    // ── PUT /api/v1/workspaces/{id} ───────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkspaceRequest req, CancellationToken ct)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        if (ws == null) return NotFound("Workspace introuvable.");

        ws.Update(req.Name, req.Description, req.Color, req.IconCode);
        workspaceRepo.Update(ws);
        await workspaceRepo.SaveAsync(ct);

        return Ok200(MapWs(ws));
    }

    // ── DELETE /api/v1/workspaces/{id} ────────────────────────────────────────
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

    // ── GET /api/v1/workspaces/{id}/members ───────────────────────────────────
    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        if (ws == null) return NotFound();

        var members = await membershipRepo.GetByWorkspaceAsync(id, ct);
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

    // ── POST /api/v1/workspaces/{id}/members ──────────────────────────────────
    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var ws = await workspaceRepo.GetByIdAsync(id, ct);
        if (ws == null) return NotFound();

        var existing = await membershipRepo.FindAsync(req.UserId, id, null, ct);
        if (existing != null)
            return Conflict("Cet utilisateur est déjà membre de cet espace de travail.");

        var membership = Membership.Create(TenantId, req.UserId,
            MembershipType.Member, workspaceId: id);

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

    // ── DELETE /api/v1/workspaces/{id}/members/{userId} ───────────────────────
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var membership = await membershipRepo.FindAsync(userId, id, null, ct);
        if (membership == null) return NotFound();

        membershipRepo.SoftDelete(membership);
        await membershipRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static WorkspaceDto MapWs(Workspace w) => new WorkspaceDto(
        w.Id,
        w.Name,
        w.Description,
        w.Type,
        w.Status,
        w.Color,
        w.IconCode,
        w.OwnerId,
        w.MemberCount,
        w.StorageUsedBytes,
        w.Tags,
        w.CreatedAt
    );
}
