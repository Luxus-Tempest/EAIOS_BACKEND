using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;

namespace EAIOS.Api.Application.Agent;

public sealed class AgentExecutionService(
    IAgentExecutionRepository executionRepo) : IAgentExecutionService
{
    public async Task<AgentExecution> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        return await executionRepo.GetByIdAsync(executionId, ct) ?? throw new KeyNotFoundException("Exécution introuvable.");
    }

    public async Task CancelExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        var execution = await GetExecutionAsync(executionId, ct);
        execution.Cancel();
        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);
    }

    public async Task SubmitHumanInputAsync(Guid executionId, string response, CancellationToken ct = default)
    {
        var execution = await GetExecutionAsync(executionId, ct);
        if (execution.Status != AgentExecutionStatus.AwaitingHumanInput)
            throw new InvalidOperationException("L'exécution n'attend pas d'intervention humaine.");

        execution.ResumeFromHumanInput(response);
        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);
    }
}
