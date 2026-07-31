using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Contrôleur de base pour tous les endpoints API v1.
/// Fournit les helpers communs : TenantId, UserId, réponses standardisées.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class V1ApiController : ControllerBase
{
    // ── Helpers contexte ───────────────────────────────────────────────────────

    protected ICurrentUser  CurrentUser   => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
    protected ITenantContext TenantCtx    => HttpContext.RequestServices.GetRequiredService<ITenantContext>();

    protected Guid TenantId => TenantCtx.IsResolved
        ? TenantCtx.OrganizationId
        : throw new InvalidOperationException("Tenant context non résolu.");

    protected Guid? ActorId => CurrentUser.UserId;

    // ── Réponses standardisées ─────────────────────────────────────────────────

    protected IActionResult Ok200<T>(T data) =>
        Ok(ApiResponse.Wrap(data));

    protected IActionResult OkList<T>(IReadOnlyList<T> items, int total, int page, int pageSize) =>
        Ok(ApiResponse.List(items, total, page, pageSize));

    protected IActionResult Created201<T>(string routeName, object routeValues, T data) =>
        CreatedAtRoute(routeName, routeValues, ApiResponse.Wrap(data));

    protected IActionResult NoContent204() => NoContent();

    protected IActionResult NotFound(string detail = "Ressource introuvable.") =>
        base.NotFound(new ProblemDetails
        {
            Status   = StatusCodes.Status404NotFound,
            Title    = "Not Found",
            Detail   = detail,
            Instance = HttpContext.Request.Path
        });

    protected IActionResult Forbidden(string detail = "Permissions insuffisantes.") =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status   = StatusCodes.Status403Forbidden,
            Title    = "Forbidden",
            Detail   = detail,
            Instance = HttpContext.Request.Path
        });

    protected IActionResult Conflict(string detail) =>
        base.Conflict(new ProblemDetails
        {
            Status   = StatusCodes.Status409Conflict,
            Title    = "Conflict",
            Detail   = detail,
            Instance = HttpContext.Request.Path
        });

    protected IActionResult UnprocessableEntity(string detail) =>
        base.UnprocessableEntity(new ProblemDetails
        {
            Status   = StatusCodes.Status422UnprocessableEntity,
            Title    = "Unprocessable Entity",
            Detail   = detail,
            Instance = HttpContext.Request.Path
        });
}
