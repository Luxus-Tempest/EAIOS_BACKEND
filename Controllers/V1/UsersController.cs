using EAIOS.Api.Application.Identity;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(
    IUserService userService,
    IUserRepository userRepo,
    ISessionRepository sessionRepo,
    IApiKeyRepository apiKeyRepo,
    IApiKeyService apiKeyService) : V1ApiController
{
    // ── GET /api/v1/users/me ──────────────────────────────────────────────────
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var user = await userRepo.GetByIdAsync(ActorId.Value, ct);
        if (user == null) return NotFound();
        return Ok200(MapUser(user));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var user = await userService.UpdateProfileAsync(ActorId.Value, req.FirstName, req.LastName, req.JobTitle, req.Department, req.Locale, req.TimeZone, ct);
            return Ok200(MapUser(user));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        
        try
        {
            await userService.ChangePasswordAsync(ActorId.Value, req.CurrentPassword, req.NewPassword, ct);
            return Ok(new { message = "Mot de passe mis à jour." });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "INVALID_PASSWORD", message = ex.Message });
        }
    }

    // ── Sessions ──────────────────────────────────────────────────────────────
    [HttpGet("me/sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var sessions = await sessionRepo.GetActiveByUserAsync(ActorId.Value, ct);
        var dtos = sessions.Select(s => new SessionDto(s.Id, s.IpAddress, s.UserAgent, s.LastActivityAt, s.CreatedAt, s.ExpiresAt, false)).ToList();
        return Ok200(dtos);
    }

    [HttpDelete("me/sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var session = await sessionRepo.GetByIdAsync(id, ct);
        if (session == null || session.UserId != ActorId.Value) return NotFound();
        session.Revoke("user_request");
        await sessionRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── API Keys ──────────────────────────────────────────────────────────────
    [HttpGet("me/api-keys")]
    public async Task<IActionResult> ListApiKeys(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var keys = await apiKeyRepo.GetByUserAsync(ActorId.Value, ct);
        var dtos = keys.Select(k => new ApiKeyDto(k.Id, k.Name, k.KeyPrefix, k.Scopes, k.IsActive, k.ExpiresAt, k.LastUsedAt, k.CreatedAt)).ToList();
        return Ok200(dtos);
    }

    [HttpPost("me/api-keys")]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var (fullKey, prefix, hash) = apiKeyService.Generate();
        var apiKey = ApiKey.Create(TenantId, ActorId.Value, req.Name, prefix, hash, req.Scopes, req.ExpiresAt);

        await apiKeyRepo.AddAsync(apiKey, ct);
        await apiKeyRepo.SaveAsync(ct);

        return Ok200(new ApiKeyCreatedDto(apiKey.Id, apiKey.Name, apiKey.KeyPrefix, fullKey, apiKey.Scopes, apiKey.ExpiresAt, apiKey.CreatedAt));
    }

    [HttpDelete("me/api-keys/{id:guid}")]
    public async Task<IActionResult> RevokeApiKey(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var key = await apiKeyRepo.GetByIdAsync(id, ct);
        if (key == null || key.UserId != ActorId.Value) return NotFound();
        apiKeyRepo.SoftDelete(key);
        await apiKeyRepo.SaveAsync(ct);
        return NoContent204();
    }

    // ── Admin (users management) ──────────────────────────────────────────────
    [HttpGet]
    [Authorize(Policy = "identity.users.manage")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null,
        [FromQuery] UserStatus? status = null,
        CancellationToken ct = default)
    {
        var result = await userRepo.SearchAsync(q, status, page, pageSize, ct);
        
        var mappedItems = result.Items.Select(MapUser).ToList();
        return Ok(EAIOS.Api.Application.Common.Models.ApiResponse.List(mappedItems, result.TotalCount, page, pageSize));
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static UserDto MapUser(User u) =>
        new(u.Id, u.OrganizationId, u.Email, u.FirstName, u.LastName, u.FullName, u.DisplayName,
            u.AvatarUrl, u.JobTitle, u.Department, u.Locale, u.TimeZone, u.Status, u.IsEmailVerified,
            u.IsMfaEnabled, u.LastLoginAt, u.CreatedAt, [], []);
}

// Missing records for compilation
public record UpdateProfileRequest(string FirstName, string LastName, string? JobTitle, string? Department, string? Locale, string? TimeZone);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
