using EAIOS.Api.Application.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Flux Server-Sent Events (SSE) pour les notifications en temps réel.
/// Route : /api/v1/realtime
/// </summary>
[Route("api/v1/realtime")]
[Authorize]
public sealed class RealtimeController(IRealtimeEventService realtimeService) : V1ApiController
{
    [HttpGet("events")]
    public async Task GetEvents(CancellationToken ct)
    {
        if (!ActorId.HasValue)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Permet au client de savoir que la connexion est établie
        await Response.WriteAsync($"event: connected\ndata: {{\"message\": \"SSE connection established\"}}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        // Cette méthode va bloquer l'exécution tant que le client est connecté
        // et pousser les messages directement dans Response.WriteAsync()
        await realtimeService.SubscribeAsync(
            TenantId, 
            ActorId.Value, 
            async (message) =>
            {
                await Response.WriteAsync(message, ct);
                await Response.Body.FlushAsync(ct);
            }, 
            ct);
    }
}
