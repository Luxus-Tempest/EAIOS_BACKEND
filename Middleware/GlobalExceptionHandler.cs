using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace EAIOS.Api.Middleware;

/// <summary>
/// Gestionnaire d'exceptions global conforme à RFC 7807 (Problem Details).
/// Convertit toutes les exceptions non-gérées en réponses JSON structurées.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext    context,
        Exception      exception,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        logger.LogError(exception,
            "Unhandled exception [{TraceId}] {Method} {Path}: {Message}",
            traceId,
            context.Request.Method,
            context.Request.Path,
            exception.Message);

        var problem = MapToProblemDetails(exception, context.Request.Path, traceId);

        context.Response.StatusCode  = problem.Status!.Value;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, ct);

        return true;
    }

    private static ProblemDetails MapToProblemDetails(Exception ex, string path, string traceId)
    {
        var pd = ex switch
        {
            UnauthorizedAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title  = "Non autorisé",
                Detail = ex.Message,
                Type   = "https://tools.ietf.org/html/rfc7235#section-3.1"
            },
            KeyNotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title  = "Ressource introuvable",
                Detail = ex.Message,
                Type   = "https://tools.ietf.org/html/rfc7231#section-6.5.4"
            },
            ArgumentException or ArgumentNullException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Requête invalide",
                Detail = ex.Message,
                Type   = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            },
            InvalidOperationException => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title  = "Opération impossible",
                Detail = ex.Message,
                Type   = "https://tools.ietf.org/html/rfc4918#section-11.2"
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title  = "Erreur interne du serveur",
                Detail = "Une erreur inattendue s'est produite. Veuillez réessayer.",
                Type   = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            }
        };

        pd.Instance = path;
        pd.Extensions["traceId"] = traceId;

        return pd;
    }
}
