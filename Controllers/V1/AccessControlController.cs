using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion fine des droits : Rôles, Permissions, Politiques ABAC, ACLs.
/// Route : /api/v1/access-control
/// </summary>
[Route("api/v1/access-control")]
[Authorize]
public sealed class AccessControlController(
    EAIOS.Api.Application.AccessControl.IAccessControlService accessControlService,
    IRoleRepository roleRepo,
    IPolicyRepository policyRepo,
    IResourceAclRepository aclRepo,
    IPermissionRepository permRepo) : V1ApiController
{
    // ── Rôles ─────────────────────────────────────────────────────────────────
    [HttpGet("roles")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
    {
        var roles = await roleRepo.GetAllAsync(ct);
        return Ok200(roles);
    }

    [HttpPost("roles")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var role = await accessControlService.CreateRoleAsync(TenantId, req.Name, req.Description, ct);
            return Ok200(role);
        }
        catch (InvalidOperationException ex) when (ex.Message == "ROLE_EXISTS")
        {
            return Conflict(new { code = "ROLE_EXISTS", message = "Un rôle avec ce nom existe déjà." });
        }
    }

    // ── Permissions ───────────────────────────────────────────────────────────
    [HttpGet("roles/{id:guid}/permissions")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> GetRolePermissions(Guid id, CancellationToken ct)
    {
        var role = await roleRepo.GetByIdAsync(id, ct);
        if (role == null) return NotFound();

        return Ok200(role.PermissionCodes); // Suppose que role.PermissionCodes expose la liste des codes ou objets
    }

    [HttpPut("roles/{id:guid}/permissions")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> UpdateRolePermissions(Guid id, [FromBody] UpdateRolePermissionsRequest req, CancellationToken ct)
    {
        try
        {
            var role = await accessControlService.UpdateRolePermissionsAsync(id, req.Permissions, ct);
            return Ok200(role);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "BUILTIN_ROLE_LOCKED")
        {
            return BadRequest(new { code = "BUILTIN_ROLE_LOCKED", message = "Impossible de modifier les permissions d'un rôle système." });
        }
    }

    // ── Policies (ABAC) ───────────────────────────────────────────────────────
    [HttpGet("policies")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> ListPolicies(CancellationToken ct)
    {
        var policies = await policyRepo.GetAllAsync(ct);
        return Ok200(policies);
    }

    [HttpPost("policies")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> CreatePolicy([FromBody] CreatePolicyRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var policy = await accessControlService.CreatePolicyAsync(TenantId, req.Name, req.Description, req.Type, req.TargetType, req.ConditionsJson, req.Effect, req.Permissions, req.Priority, ActorId.Value, ct);
        return Ok200(policy);
    }

    // ── ACLs ──────────────────────────────────────────────────────────────────
    [HttpGet("acls")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> ListAcls([FromQuery] Guid resourceId, [FromQuery] string resourceType, CancellationToken ct)
    {
        var acls = await aclRepo.GetByResourceAsync(resourceId, resourceType, ct);
        return Ok200(acls);
    }

    [HttpPost("acls")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> CreateAcl([FromBody] CreateAclRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var acl = await accessControlService.CreateAclAsync(TenantId, req.ResourceId, req.ResourceType, req.PrincipalId, req.PrincipalType, req.Permissions, req.Effect, req.PermissionLevel, ActorId.Value, ct);
        return Ok200(acl);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public record CreateRoleRequest(string Name, string? Description);
public record UpdateRolePermissionsRequest(string[] Permissions);
public record CreatePolicyRequest(string Name, string? Description, PolicyType Type, string TargetType, string ConditionsJson, PolicyEffect Effect, string[] Permissions, int Priority);
public record CreateAclRequest(Guid ResourceId, string ResourceType, Guid PrincipalId, PrincipalType PrincipalType, string[] Permissions, AclEffect Effect, string PermissionLevel);
