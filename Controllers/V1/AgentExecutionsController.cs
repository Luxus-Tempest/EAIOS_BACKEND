using EAIOS.Api.Application.Agent;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Supervision des exécutions d'agents IA, décision humaine, et fils de conversation.
/// Route : /api/v1/executions
///
/// Un fil (« session ») regroupe les tours d'une même conversation : c'est ce que
/// la personne retrouve dans son historique et peut poursuivre. Chaque tour est
/// une exécution.
/// </summary>
[Route("api/v1/executions")]
[Authorize]
public sealed class AgentExecutionsController(
    IAgentExecutionService executionService,
    IAgentRepository agentRepo) : V1ApiController
{
    // ═════════════════════════════════════════════════════════════════════════
    // FILS DE CONVERSATION
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Mes conversations, toutes agents confondus — épinglées d'abord, puis les plus récentes.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(
        [FromQuery] Guid? agentId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var result = await executionService.ListConversationsAsync(ActorId.Value, agentId, q, page, pageSize, ct);

        // Le nom de l'agent accompagne chaque fil : l'historique se lit sans
        // ouvrir le catalogue. Une requête par agent distinct, jamais par ligne.
        var names = new Dictionary<Guid, string?>();
        foreach (var id in result.Items.Select(c => c.AgentId).Distinct())
            names[id] = (await agentRepo.GetByIdAsync(id, ct))?.DisplayName is { Length: > 0 } dn
                ? dn
                : (await agentRepo.GetByIdAsync(id, ct))?.Name;

        return OkList(
            result.Items.Select(c => ExecutionViews.Map(c, names.GetValueOrDefault(c.AgentId))).ToList(),
            result.TotalCount, page, pageSize);
    }

    /// <summary>Un fil et tous ses tours, du premier au dernier.</summary>
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSession(Guid sessionId, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var (conversation, turns) = await executionService.GetConversationAsync(sessionId, ActorId.Value, ct);
            var agent = await agentRepo.GetByIdAsync(conversation.AgentId, ct);
            var name = agent?.DisplayName is { Length: > 0 } dn ? dn : agent?.Name;

            return Ok200(new ConversationDetailView(
                ExecutionViews.Map(conversation, name),
                turns.Select(ExecutionViews.Map).ToList()));
        }
        catch (KeyNotFoundException) { return NotFound("Conversation introuvable."); }
    }

    /// <summary>Renommer ou épingler un fil.</summary>
    [HttpPut("sessions/{sessionId:guid}")]
    public async Task<IActionResult> UpdateSession(Guid sessionId, [FromBody] UpdateConversationRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            var conversation = await executionService.UpdateConversationAsync(sessionId, ActorId.Value, req.Title, req.IsPinned, ct);
            return Ok200(ExecutionViews.Map(conversation));
        }
        catch (KeyNotFoundException) { return NotFound("Conversation introuvable."); }
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        try
        {
            await executionService.DeleteConversationAsync(sessionId, ActorId.Value, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException) { return NotFound("Conversation introuvable."); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EXÉCUTIONS
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExecution(Guid id, CancellationToken ct)
    {
        try
        {
            var execution = await executionService.GetExecutionAsync(id, ct);
            return Ok200(ExecutionViews.Map(execution));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelExecution(Guid id, CancellationToken ct)
    {
        try
        {
            await executionService.CancelExecutionAsync(id, ct);
            return Ok(new { message = "Exécution annulée." });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// La décision de la personne. La réponse est l'exécution après reprise :
    /// aboutie (avec sa réponse et ses citations), de nouveau en attente (nouvelle
    /// décision), ou en échec. L'écran n'a pas à recharger pour le savoir.
    /// </summary>
    [HttpPost("{id:guid}/input")]
    public async Task<IActionResult> SubmitHumanInput(Guid id, [FromBody] HumanInputRequest req, CancellationToken ct)
    {
        try
        {
            var execution = await executionService.SubmitHumanInputAsync(id, req.Response, req.Comment, ct);
            return Ok200(ExecutionViews.Map(execution));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }
}
