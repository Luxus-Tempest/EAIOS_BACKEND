using EAIOS.Api.Application.Notification;
using EAIOS.Api.Domain.Notification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Gestion des templates de notifications.
/// Route : /api/v1/notifications/templates
/// </summary>
[Route("api/v1/notifications/templates")]
[Authorize]
public sealed class NotificationTemplatesController(
    INotificationTemplateService templateService) : V1ApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var templates = await templateService.GetTemplatesAsync(ct);
        return Ok200(templates.Select(MapTemplate).ToList());
    }

    [HttpGet("{id:guid}", Name = "GetNotificationTemplate")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        try
        {
            var template = await templateService.GetTemplateAsync(id, ct);
            return Ok200(MapTemplate(template));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTemplateRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var template = await templateService.CreateTemplateAsync(
            TenantId, req.EventType, req.Channel, req.Language, req.SubjectTemplate, req.BodyTemplate, ActorId.Value, req.IsSystem, ct);
            
        return Created201("GetNotificationTemplate", new { id = template.Id }, MapTemplate(template));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTemplateRequest req, CancellationToken ct)
    {
        try
        {
            var template = await templateService.UpdateTemplateAsync(id, req.SubjectTemplate, req.BodyTemplate, req.IsActive, ct);
            return Ok200(MapTemplate(template));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await templateService.DeleteTemplateAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static object MapTemplate(NotificationTemplate t) => new
    {
        t.Id, t.EventType, t.Channel, t.Language, t.SubjectTemplate, t.BodyTemplate, t.IsSystem, t.IsActive, t.CreatedAt
    };
}
