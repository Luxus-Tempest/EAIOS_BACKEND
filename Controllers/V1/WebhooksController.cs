using EAIOS.Api.Application.Webhook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion des abonnements webhooks (sortants).
/// Nécessite les permissions d'administration au niveau de l'organisation.
/// Route : /api/v1/webhooks
/// </summary>
[Route("api/v1/webhooks")]
[Authorize(Roles = "platform.admin,Admin")]
public sealed class WebhooksController(
    IWebhookService webhookService) : V1ApiController
{
    [HttpGet]
    public async Task<IActionResult> ListWebhooks(CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var subs = await webhookService.ListSubscriptionsAsync(TenantId, ct);
        return Ok200(subs);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWebhook(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var sub = await webhookService.GetSubscriptionAsync(id, TenantId, ct);
            return Ok200(sub);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateWebhook([FromBody] CreateWebhookRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var sub = await webhookService.CreateSubscriptionAsync(TenantId, req, ActorId.Value, ct);
        return Created201("GetWebhook", new { id = sub.Id }, sub);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateWebhook(Guid id, [FromBody] UpdateWebhookRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var sub = await webhookService.UpdateSubscriptionAsync(id, TenantId, req, ActorId.Value, ct);
            return Ok200(sub);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWebhook(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            await webhookService.DeleteSubscriptionAsync(id, TenantId, ActorId.Value, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestWebhook(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var success = await webhookService.TestSubscriptionAsync(id, TenantId, ct);
            return Ok200(new { Success = success });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
