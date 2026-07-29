using EAIOS.Api.Contracts;
using EAIOS.Api.Domain;
using EAIOS.Api.Infrastructure;
using EAIOS.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[Route("v1/workspaces")]
public sealed class WorkspacesController(InMemoryEaiosStore store, CurrentTenant tenant) : V1ApiController(tenant)
{
    [HttpGet]
    public IActionResult List() => !IsAuthenticated ? AuthenticationRequired() : Ok(new { data = store.Workspaces.Values.Where(w => w.OrganizationId == TenantId && !w.IsDeleted && (IsOrganizationAdmin || w.MemberIds.Contains(User.UserId()))).OrderBy(w => w.Name) });

    [HttpPost]
    public IActionResult Create(CreateWorkspaceRequest request)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!IsOrganizationAdmin) return Forbidden();
        var workspace = new Workspace { OrganizationId = TenantId, Name = request.Name.Trim(), Description = request.Description, Type = request.Type };
        workspace.MemberIds.Add(User.UserId());
        store.Workspaces[workspace.Id] = workspace;
        return Created($"/v1/workspaces/{workspace.Id}", new { data = workspace });
    }

    [HttpGet("{workspaceId:guid}")]
    public IActionResult Get(Guid workspaceId)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!TryGet(workspaceId, out var workspace)) return NotFound();
        return IsOrganizationAdmin || workspace.MemberIds.Contains(User.UserId()) ? Ok(new { data = workspace }) : Forbidden();
    }

    [HttpPut("{workspaceId:guid}")]
    public IActionResult Update(Guid workspaceId, UpdateWorkspaceRequest request)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!TryGet(workspaceId, out var workspace)) return NotFound();
        if (!IsOrganizationAdmin) return Forbidden();
        workspace.Name = request.Name.Trim(); workspace.Description = request.Description; workspace.Type = request.Type ?? workspace.Type;
        return Ok(new { data = workspace });
    }

    [HttpDelete("{workspaceId:guid}")]
    public IActionResult Delete(Guid workspaceId)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!IsOrganizationAdmin) return Forbidden();
        if (!TryGet(workspaceId, out var workspace)) return NotFound();
        workspace.IsDeleted = true;
        return NoContent();
    }

    [HttpGet("{workspaceId:guid}/members")]
    public IActionResult Members(Guid workspaceId)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!TryGet(workspaceId, out var workspace)) return NotFound();
        if (!IsOrganizationAdmin && !workspace.MemberIds.Contains(User.UserId())) return Forbidden();
        return Ok(new { data = store.Users.Values.Where(u => workspace.MemberIds.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }) });
    }

    [HttpPost("{workspaceId:guid}/members")]
    public IActionResult AddMember(Guid workspaceId, AddMemberRequest request)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!IsOrganizationAdmin) return Forbidden();
        if (!TryGet(workspaceId, out var workspace)) return NotFound();
        if (!store.Users.TryGetValue(request.UserId, out var user) || user.OrganizationId != TenantId) return NotFound(new ProblemDetails { Status = 404, Title = "User not found in organization" });
        workspace.MemberIds.Add(request.UserId);
        return NoContent();
    }

    [HttpDelete("{workspaceId:guid}/members/{userId:guid}")]
    public IActionResult RemoveMember(Guid workspaceId, Guid userId)
    {
        if (!IsAuthenticated) return AuthenticationRequired();
        if (!IsOrganizationAdmin) return Forbidden();
        if (!TryGet(workspaceId, out var workspace)) return NotFound();
        workspace.MemberIds.Remove(userId);
        return NoContent();
    }

    private bool TryGet(Guid id, out Workspace workspace) => store.Workspaces.TryGetValue(id, out workspace!) && workspace.OrganizationId == TenantId && !workspace.IsDeleted;
}
