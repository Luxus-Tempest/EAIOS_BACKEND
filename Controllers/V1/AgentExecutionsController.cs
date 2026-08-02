using EAIOS.Api.Application.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Supervision des exécutions d'agents IA, annulation et input humain.
/// Route : /api/v1/executions
/// </summary>
[Route("api/v1/executions")]
[Authorize]
public sealed class AgentExecutionsController(
    IAgentExecutionService executionService) : V1ApiController
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExecution(Guid id, CancellationToken ct)
    {
        try
        {
            var execution = await executionService.GetExecutionAsync(id, ct);
            return Ok200(new
            {
                execution.Id,
                execution.AgentId,
                execution.Status,
                Input = execution.InputText,
                Output = execution.OutputText,
                execution.PromptTokens,
                execution.CompletionTokens,
                execution.CostUsd,
                DurationMs = execution.Duration?.TotalMilliseconds,
                execution.ErrorMessage,
                execution.RequiresHumanInput,
                execution.StartedAt,
                execution.CompletedAt
            });
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

    [HttpPost("{id:guid}/input")]
    public async Task<IActionResult> SubmitHumanInput(Guid id, [FromBody] HumanInputRequest req, CancellationToken ct)
    {
        try
        {
            await executionService.SubmitHumanInputAsync(id, req.Response, ct);
            return Ok(new { message = "Input soumis avec succès, l'exécution reprend." });
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
