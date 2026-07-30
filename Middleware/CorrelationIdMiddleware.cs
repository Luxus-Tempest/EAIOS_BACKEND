using EAIOS.Api.Application.Common.Interfaces;

namespace EAIOS.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        const string headerName = "X-Correlation-ID";

        if (!context.Request.Headers.TryGetValue(headerName, out var correlationId) || string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString("N");
        }

        context.Items[headerName] = correlationId.ToString();
        context.Response.Headers[headerName] = correlationId.ToString();

        await next(context);
    }
}
