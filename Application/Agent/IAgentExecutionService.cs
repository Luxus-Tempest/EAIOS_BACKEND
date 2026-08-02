using EAIOS.Api.Domain.Agent;

namespace EAIOS.Api.Application.Agent;

public interface IAgentExecutionService
{
    Task<AgentExecution> GetExecutionAsync(Guid executionId, CancellationToken ct = default);
    Task CancelExecutionAsync(Guid executionId, CancellationToken ct = default);
    Task SubmitHumanInputAsync(Guid executionId, string response, CancellationToken ct = default);
}
