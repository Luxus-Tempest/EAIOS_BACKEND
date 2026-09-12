using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Agent;

namespace EAIOS.Api.Application.Agent;

public interface IAgentExecutionService
{
    Task<AgentExecution> GetExecutionAsync(Guid executionId, CancellationToken ct = default);
    Task CancelExecutionAsync(Guid executionId, CancellationToken ct = default);

    /// <summary>
    /// Reprend une exécution arrêtée avec la décision de la personne, et rend
    /// l'exécution telle qu'elle est après la reprise : aboutie, de nouveau en
    /// attente, ou en échec.
    /// </summary>
    Task<AgentExecution> SubmitHumanInputAsync(Guid executionId, string response, string? comment = null, CancellationToken ct = default);

    // ── Fils de conversation ──────────────────────────────────────────────────

    Task<PagedResult<AgentConversation>> ListConversationsAsync(Guid userId, Guid? agentId, string? q, int page, int pageSize, CancellationToken ct = default);
    Task<(AgentConversation Conversation, IReadOnlyList<AgentExecution> Turns)> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
    Task<AgentConversation> UpdateConversationAsync(Guid conversationId, Guid userId, string? title, bool? isPinned, CancellationToken ct = default);
    Task DeleteConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
}
