using EAIOS.Api.Contracts;
using EAIOS.Api.Domain;
using EAIOS.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

[ApiController]
[Route("v1/auth")]
public sealed class AuthController(InMemoryEaiosStore store, TokenService tokens, IHostEnvironment environment) : ControllerBase
{
    /// <remarks>Development bootstrap only. Production account creation is invitation-only per the specification.</remarks>
    [HttpPost("bootstrap")]
    public ActionResult<object> Bootstrap(BootstrapOrganizationRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (store.Users.Values.Any(user => string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
            return Conflict(Problem("Email already exists."));
        var organization = new Organization { Name = request.OrganizationName };
        var user = new User { OrganizationId = organization.Id, Email = request.Email.Trim().ToLowerInvariant(), FirstName = request.FirstName, LastName = request.LastName, PasswordHash = TokenService.HashPassword(request.Password) };
        user.Roles.Add("org.admin");
        store.Organizations[organization.Id] = organization;
        store.Users[user.Id] = user;
        return Created($"/v1/organization", new { data = new { organizationId = organization.Id, userId = user.Id } });
    }

    [HttpPost("login")]
    public ActionResult<object> Login(LoginRequest request, [FromHeader(Name = "X-Tenant-ID")] Guid tenantId)
    {
        var user = store.FindUser(tenantId, request.Email);
        if (user is null || !user.IsActive || !TokenService.VerifyPassword(request.Password, user.PasswordHash))
            return Unauthorized(Problem("Invalid credentials.", StatusCodes.Status401Unauthorized));
        return Ok(new { data = CreateResponse(user) });
    }

    [HttpPost("refresh")]
    public ActionResult<object> Refresh(RefreshRequest request)
    {
        var hash = TokenService.Hash(request.RefreshToken);
        var session = store.Sessions.Values.SingleOrDefault(s => s.RefreshTokenHash == hash && !s.IsRevoked && s.ExpiresAt > DateTimeOffset.UtcNow);
        if (session is null || !store.Users.TryGetValue(session.UserId, out var user) || !user.IsActive)
            return Unauthorized(Problem("Invalid, expired, or revoked refresh token.", StatusCodes.Status401Unauthorized));
        session.IsRevoked = true; // rotation
        return Ok(new { data = CreateResponse(user) });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var userId = HttpContext.User.Identity?.IsAuthenticated == true ? Guid.Parse(HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value) : Guid.Empty;
        foreach (var session in store.Sessions.Values.Where(s => s.UserId == userId)) session.IsRevoked = true;
        return NoContent();
    }

    private TokenResponse CreateResponse(User user)
    {
        var session = new Session { UserId = user.Id, RefreshTokenHash = string.Empty, ExpiresAt = DateTimeOffset.UtcNow };
        var pair = tokens.Issue(user, session.Id);
        session.RefreshTokenHash = TokenService.Hash(pair.RefreshToken);
        session.ExpiresAt = pair.RefreshTokenExpiresAt;
        store.Sessions[session.Id] = session;
        return new TokenResponse(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt, pair.RefreshTokenExpiresAt, "Bearer", new { user.Id, user.Email, user.FirstName, user.LastName, organizationId = user.OrganizationId, roles = user.Roles, mfaRequired = false });
    }

    private ProblemDetails Problem(string detail, int status = StatusCodes.Status400BadRequest) => new() { Status = status, Title = "Request rejected", Detail = detail, Instance = HttpContext.Request.Path };
}
