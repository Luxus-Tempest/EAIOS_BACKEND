using System.Collections.Concurrent;

namespace EAIOS.Api.Middleware;

/// <summary>
/// Rate Limiting en mémoire par IP + route.
/// Stratégie : Token Bucket — chaque client reçoit N tokens/minute.
/// En production, remplacer par Microsoft.AspNetCore.RateLimiting ou Redis.
/// </summary>
public sealed class RateLimitingMiddleware(
    RequestDelegate next,
    ILogger<RateLimitingMiddleware> logger,
    IConfiguration configuration)
{
    // Règles par route prefix : (MaxRequests, WindowSeconds)
    private static readonly Dictionary<string, (int Max, int Window)> _routeRules = new()
    {
        { "/api/v1/auth/login",    (10,  60) },   // 10 req/min
        { "/api/v1/auth/register", (5,   60) },   // 5 req/min
        { "/api/v1/search/ask",    (30,  60) },   // 30 req/min (RAG)
        { "/api/v1/agents",        (60,  60) },   // 60 req/min
    };
    private static readonly (int Max, int Window) _default = (200, 60); // 200 req/min par défaut

    private static readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var ip    = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path  = context.Request.Path.Value?.ToLowerInvariant() ?? "/";

        var (max, window) = GetRule(path);
        var bucketKey     = $"{ip}:{path}";

        var bucket = _buckets.GetOrAdd(bucketKey, _ => new TokenBucket(max, window));

        if (!bucket.TryConsume())
        {
            logger.LogWarning("Rate limit exceeded for {IP} on {Path}", ip, path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("Retry-After", window.ToString());
            context.Response.Headers.Append("X-RateLimit-Limit", max.ToString());
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = 429,
                Title  = "Trop de requêtes",
                Detail = $"Limite de {max} requêtes par {window} secondes atteinte.",
                Type   = "https://tools.ietf.org/html/rfc6585#section-4"
            });
            return;
        }

        context.Response.Headers.Append("X-RateLimit-Limit",     max.ToString());
        context.Response.Headers.Append("X-RateLimit-Remaining", bucket.Available.ToString());

        await next(context);
    }

    private static (int Max, int Window) GetRule(string path)
    {
        foreach (var (prefix, rule) in _routeRules)
            if (path.StartsWith(prefix)) return rule;
        return _default;
    }
}

internal sealed class TokenBucket(int capacity, int windowSeconds)
{
    private readonly object _lock    = new();
    private int             _tokens  = capacity;
    private DateTime        _resetAt = DateTime.UtcNow.AddSeconds(windowSeconds);

    public int Available
    {
        get { lock (_lock) { RefillIfNeeded(); return _tokens; } }
    }

    public bool TryConsume()
    {
        lock (_lock)
        {
            RefillIfNeeded();
            if (_tokens <= 0) return false;
            _tokens--;
            return true;
        }
    }

    private void RefillIfNeeded()
    {
        if (DateTime.UtcNow >= _resetAt)
        {
            _tokens  = capacity;
            _resetAt = DateTime.UtcNow.AddSeconds(windowSeconds);
        }
    }
}
