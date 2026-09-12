using EAIOS.Api.Application.AccessControl;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion fine des droits : rôles et leurs titulaires, catalogue de permissions,
/// politiques ABAC, ACL de ressources.
/// Route : /api/v1/access-control
///
/// Les réponses passent par les DTO d'<c>Application/AccessControl/Dtos.cs</c> :
/// l'API ne renvoie jamais une entité brute (champs internes, couplage au modèle).
/// </summary>
[Route("api/v1/access-control")]
[Authorize]
public sealed class AccessControlController(
    IAccessControlService accessControlService,
    IRoleRepository roleRepo,
    IPolicyRepository policyRepo,
    IResourceAclRepository aclRepo) : V1ApiController
{
    // ═════════════════════════════════════════════════════════════════════════
    // RÔLES
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("roles")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
    {
        var roles = await roleRepo.GetAllAsync(ct);
        return Ok200(roles.Select(MapRole).ToList());
    }

    [HttpGet("roles/{id:guid}", Name = "GetRole")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> GetRole(Guid id, CancellationToken ct)
    {
        var role = await roleRepo.GetByIdAsync(id, ct);
        return role == null ? NotFound() : Ok200(MapRole(role));
    }

    [HttpPost("roles")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var role = await accessControlService.CreateRoleAsync(TenantId, req.Name, req.Description, ct);

            // Le contrat accepte des permissions à la création : on les pose dans
            // la foulée plutôt que d'exiger un second appel.
            if (req.PermissionCodes is { Length: > 0 })
                role = await accessControlService.UpdateRolePermissionsAsync(role.Id, req.PermissionCodes, ct);

            return Created201("GetRole", new { id = role.Id }, MapRole(role));
        }
        catch (InvalidOperationException ex) when (ex.Message == "ROLE_EXISTS")
        {
            return Conflict(new { code = "ROLE_EXISTS", message = "Un rôle avec ce nom existe déjà." });
        }
    }

    [HttpPut("roles/{id:guid}")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest req, CancellationToken ct)
    {
        try
        {
            var role = await accessControlService.UpdateRoleAsync(id, req.DisplayName, req.Description, req.Color, ct);
            return Ok200(MapRole(role));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("roles/{id:guid}")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        try
        {
            await accessControlService.DeleteRoleAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) when (ex.Message == "BUILTIN_ROLE_LOCKED")
        {
            return Conflict("Un rôle système ne se supprime pas.");
        }
    }

    // ── Permissions d'un rôle ─────────────────────────────────────────────────

    [HttpGet("roles/{id:guid}/permissions")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> GetRolePermissions(Guid id, CancellationToken ct)
    {
        var role = await roleRepo.GetByIdAsync(id, ct);
        if (role == null) return NotFound();

        return Ok200(role.PermissionCodes);
    }

    [HttpPut("roles/{id:guid}/permissions")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> UpdateRolePermissions(Guid id, [FromBody] SetRolePermissionsRequest req, CancellationToken ct)
    {
        try
        {
            var role = await accessControlService.UpdateRolePermissionsAsync(id, req.PermissionCodes, ct);
            return Ok200(MapRole(role));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) when (ex.Message == "BUILTIN_ROLE_LOCKED")
        {
            return BadRequest(new { code = "BUILTIN_ROLE_LOCKED", message = "Impossible de modifier les permissions d'un rôle système." });
        }
    }

    // ── Titulaires d'un rôle ──────────────────────────────────────────────────

    /// <summary>Les personnes qui portent ce rôle, attribution par attribution.</summary>
    [HttpGet("roles/{id:guid}/users")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> GetRoleHolders(Guid id, CancellationToken ct)
    {
        var role = await roleRepo.GetByIdAsync(id, ct);
        if (role == null) return NotFound();

        var holders = await accessControlService.GetRoleHoldersAsync(id, ct);
        return Ok200(holders.Select(MapUserRole).ToList());
    }

    /// <summary>
    /// Attribue le rôle à une personne. C'était le chaînon manquant : sans cet
    /// endpoint, un compte ne recevait un rôle qu'à l'inscription par invitation.
    /// </summary>
    [HttpPost("roles/{id:guid}/users")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleToUserRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var assignment = await accessControlService.AssignRoleAsync(
                TenantId, id, req.UserId, ActorId.Value, req.WorkspaceId, req.DepartmentId, req.ExpiresAt, ct);
            return Ok200(MapUserRole(assignment));
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return UnprocessableEntity(ex.Message); }
        catch (InvalidOperationException ex) when (ex.Message == "ROLE_ALREADY_ASSIGNED")
        {
            return Conflict("Cette personne porte déjà ce rôle sur cette portée.");
        }
    }

    [HttpDelete("roles/{id:guid}/users/{userId:guid}")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> RevokeRole(Guid id, Guid userId, CancellationToken ct)
    {
        try
        {
            await accessControlService.RevokeRoleAsync(id, userId, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
    }

    /// <summary>Les rôles d'une personne, avec leur portée et leur échéance.</summary>
    [HttpGet("users/{userId:guid}/roles")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> GetUserRoles(Guid userId, CancellationToken ct)
    {
        var assignments = await accessControlService.GetUserRolesAsync(userId, ct);
        return Ok200(assignments.Select(MapUserRole).ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CATALOGUE DE PERMISSIONS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toutes les permissions que le produit connaît, groupées par module. Le
    /// frontend en tenait une copie manuelle faute d'endpoint.
    /// </summary>
    [HttpGet("permissions")]
    public IActionResult GetPermissionCatalog() =>
        Ok200(accessControlService.GetPermissionCatalog());

    // ═════════════════════════════════════════════════════════════════════════
    // POLITIQUES (ABAC)
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("policies")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> ListPolicies(CancellationToken ct)
    {
        var policies = await policyRepo.GetAllAsync(ct);
        return Ok200(policies.Select(MapPolicy).ToList());
    }

    [HttpGet("policies/{id:guid}", Name = "GetPolicy")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> GetPolicy(Guid id, CancellationToken ct)
    {
        var policy = await policyRepo.GetByIdAsync(id, ct);
        return policy == null ? NotFound() : Ok200(MapPolicy(policy));
    }

    [HttpPost("policies")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> CreatePolicy([FromBody] CreatePolicyRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var policy = await accessControlService.CreatePolicyAsync(
            TenantId, req.Name, req.Description, req.Type, req.TargetType, req.ConditionsJson,
            req.Effect, req.Permissions, req.Priority, ActorId.Value, ct);

        if (req.PrincipalType.HasValue || req.PrincipalId is not null)
            policy = await accessControlService.UpdatePolicyAsync(
                policy.Id, null, null, null, null, null, null, null,
                req.PrincipalType, req.PrincipalId, null, ct);

        return Created201("GetPolicy", new { id = policy.Id }, MapPolicy(policy));
    }

    [HttpPut("policies/{id:guid}")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> UpdatePolicy(Guid id, [FromBody] UpdatePolicyRequest req, CancellationToken ct)
    {
        try
        {
            var policy = await accessControlService.UpdatePolicyAsync(
                id, req.Name, req.Description, req.Effect, req.Permissions, req.Condition, req.IsActive,
                req.Priority, req.PrincipalType, req.PrincipalId, req.ResourceType, ct);
            return Ok200(MapPolicy(policy));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("policies/{id:guid}")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken ct)
    {
        try
        {
            await accessControlService.DeletePolicyAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ACL
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("acls")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> ListAcls([FromQuery] Guid resourceId, [FromQuery] string resourceType, CancellationToken ct)
    {
        var acls = await aclRepo.GetByResourceAsync(resourceId, resourceType, ct);
        return Ok200(acls.Select(MapAcl).ToList());
    }

    [HttpPost("acls")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> CreateAcl([FromBody] CreateAclRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var acl = await accessControlService.CreateAclAsync(
            TenantId, req.ResourceId, req.ResourceType, req.PrincipalId, req.PrincipalType,
            req.Permissions, req.Effect, req.PermissionLevel, ActorId.Value, ct);
        return Ok200(MapAcl(acl));
    }

    [HttpDelete("acls/{id:guid}")]
    [Authorize(Policy = "access_control.manage")]
    public async Task<IActionResult> DeleteAcl(Guid id, CancellationToken ct)
    {
        try
        {
            await accessControlService.DeleteAclAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MAPPERS
    // ═════════════════════════════════════════════════════════════════════════

    private static RoleDto MapRole(Role r) => new(
        r.Id, r.Name, r.DisplayName, r.Description, r.Scope, r.IsSystem, r.IsDefault,
        r.PermissionCodes, r.Color, r.UserCount, r.CreatedAt);

    private static UserRoleDto MapUserRole(UserRole ur) => new(
        ur.Id, ur.UserId, ur.RoleId, ur.RoleName, ur.WorkspaceId, ur.DepartmentId,
        ur.ExpiresAt, ur.AssignedBy, ur.CreatedAt);

    private static PolicyDto MapPolicy(Policy p) => new(
        p.Id, p.Name, p.Description, p.Type, p.Effect, p.PrincipalType, p.PrincipalId,
        p.Permissions, p.ResourceType, p.Condition, p.IsActive, p.Priority, p.CreatedAt);

    private static ResourceAclDto MapAcl(ResourceAcl a) => new(
        a.Id, a.ResourceId, a.ResourceType, a.PrincipalType, a.PrincipalId,
        a.Permissions, a.Effect, a.ExpiresAt, a.GrantedBy, a.CreatedAt);
}

// ── Contrats d'entrée propres à ce contrôleur ────────────────────────────────
// Les formes d'écriture sont celles que le frontend envoie déjà
// (`targetType`, `conditionsJson`, `priority`, `permissionLevel`) : on les garde.

public record CreateRoleRequest(string Name, string? Description, string[]? PermissionCodes = null);
public record AssignRoleToUserRequest(Guid UserId, Guid? WorkspaceId = null, Guid? DepartmentId = null, DateTime? ExpiresAt = null);
public record CreatePolicyRequest(string Name, string? Description, PolicyType Type, string TargetType, string ConditionsJson, PolicyEffect Effect, string[] Permissions, int Priority, PrincipalType? PrincipalType = null, string? PrincipalId = null);
public record UpdatePolicyRequest(string? Name, string? Description, PolicyEffect? Effect, string[]? Permissions, string? Condition, bool? IsActive, int? Priority, PrincipalType? PrincipalType, string? PrincipalId, string? ResourceType);
public record CreateAclRequest(Guid ResourceId, string ResourceType, Guid PrincipalId, PrincipalType PrincipalType, string[] Permissions, AclEffect Effect, string PermissionLevel);
