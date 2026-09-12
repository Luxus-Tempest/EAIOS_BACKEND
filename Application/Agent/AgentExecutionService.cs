using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Application.Workflow;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using EAIOS.Api.Infrastructure.Security;

namespace EAIOS.Api.Application.Agent;

/// <summary>
/// Supervision des exécutions : consultation, annulation, reprise après décision,
/// et fils de conversation.
/// </summary>
public sealed class AgentExecutionService(
    IAgentExecutionRepository executionRepo,
    IAgentRepository agentRepo,
    IAgentContextService contextService,
    IAgentRuntimeClient runtime,
    IAgentDecisionService decisions,
    IHttpContextAccessor httpContextAccessor,
    IAgentConversationRepository conversations,
    IServiceProvider? services = null) : IAgentExecutionService
{
    public async Task<AgentExecution> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        return await executionRepo.GetByIdAsync(executionId, ct) ?? throw new KeyNotFoundException("Exécution introuvable.");
    }

    public async Task CancelExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        var execution = await GetExecutionAsync(executionId, ct);
        var awaiting  = execution.Status == AgentExecutionStatus.AwaitingHumanInput;
        execution.Cancel();
        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);
        await TouchConversationAsync(execution, ct);

        // Annuler une exécution qui attendait une décision, c'est la trancher par
        // la négative : la tâche ne doit pas rester dans la file.
        if (awaiting && execution.UserId is { } actor)
            await decisions.CloseAsync(execution, actor, "cancelled", "Exécution annulée.", ct);
    }

    /// <summary>
    /// Reprend une exécution arrêtée, avec la décision de l'humain.
    ///
    /// <para>
    /// Côté runtime, <c>Command(resume=…)</c> repart <b>exactement</b> au point
    /// d'arrêt : rien n'est rejoué avant l'<c>interrupt()</c>. C'est pour cela
    /// que la reprise ne réémet pas l'entrée initiale — elle n'a pas à le faire,
    /// le fil la porte déjà.
    /// </para>
    /// <para>
    /// La portée est <b>réémise</b>, pas réutilisée : le jeton d'origine a une
    /// durée de vie courte, et une décision humaine peut arriver des heures plus
    /// tard. Ce sont les droits de la personne qui reprend, au moment où elle
    /// reprend, qui font foi.
    /// </para>
    /// </summary>
    public async Task<AgentExecution> SubmitHumanInputAsync(Guid executionId, string response, string? comment = null, CancellationToken ct = default)
    {
        var execution = await GetExecutionAsync(executionId, ct);

        if (execution.Status != AgentExecutionStatus.AwaitingHumanInput)
            throw new InvalidOperationException("L'exécution n'attend pas d'intervention humaine.");

        var agent = await agentRepo.GetByIdAsync(execution.AgentId, ct)
            ?? throw new KeyNotFoundException("Agent introuvable.");

        var actorId = execution.UserId
            ?? throw new InvalidOperationException("MISSING_ACTOR");

        execution.ResumeFromHumanInput(response);
        RuntimeDecisionRequest? pendingDecision = null;

        try
        {
            // Le fil est celui de la conversation : la reprise doit retrouver le
            // même `thread_id` que l'arrêt, sinon elle repartirait de rien.
            var context = contextService.Issue(
                execution.OrganizationId, actorId, agent, execution.AgentVersion, execution.Id, execution.SessionId);

            var outcome = new RuntimeDecisionOutcome(response, comment, actorId);
            var result  = await runtime.ResumeAsync(context.Token, CallerToken(), execution.Id, outcome, ct);

            // Le même miroir que le premier appel : une exécution reprise doit
            // produire exactement les mêmes champs qu'une exécution menée d'un
            // trait. Elle peut aussi s'arrêter de nouveau — un agent a le droit
            // de demander deux décisions.
            ExecutionMirror.Apply(execution, result);
            pendingDecision = result.IsInterrupted ? result.Interrupt : null;
        }
        catch (AgentRuntimeUnavailableException ex)
        {
            execution.Fail("RUNTIME_UNAVAILABLE", ex.Message);
        }

        executionRepo.Update(execution);
        await executionRepo.SaveAsync(ct);
        await TouchConversationAsync(execution, ct);

        // La décision est prise, d'où qu'elle vienne : la tâche de la file se
        // ferme avec elle. Venue de la file, elle est déjà close — sans effet.
        await decisions.CloseAsync(execution, actorId, response, comment, ct);

        // Reprendre ne veut pas dire terminer : un agent a le droit de demander
        // une deuxième décision, et celle-ci doit atteindre quelqu'un comme la
        // première.
        if (execution.Status == AgentExecutionStatus.AwaitingHumanInput && pendingDecision is not null)
            await decisions.OpenAsync(execution, pendingDecision, ct);

        await PublishStatusAsync(execution);

        // Un nœud « agent » de workflow attendait cette décision : l'instance reprend.
        if (execution.WorkflowInstanceId is { } instanceId && execution.Status != AgentExecutionStatus.AwaitingHumanInput
            && services?.GetService(typeof(IWorkflowAgentContinuation)) is IWorkflowAgentContinuation workflows)
            await workflows.ContinueAfterAgentAsync(instanceId, execution, ct);

        return execution;
    }

    /// <summary>La fiche d'exécution et la liste se mettent à jour sans rechargement.</summary>
    private async Task PublishStatusAsync(AgentExecution execution)
    {
        if (services?.GetService(typeof(EAIOS.Api.Application.Realtime.IRealtimeEventService)) is not EAIOS.Api.Application.Realtime.IRealtimeEventService realtime
            || execution.UserId is not { } user) return;
        await realtime.PublishToUserAsync(execution.OrganizationId, user, "agent.completed",
            new { executionId = execution.Id, status = execution.Status.ToString(), sessionId = execution.SessionId });
    }

    // ── Fils de conversation ──────────────────────────────────────────────────

    public async Task<PagedResult<AgentConversation>> ListConversationsAsync(Guid userId, Guid? agentId, string? q, int page, int pageSize, CancellationToken ct = default) =>
        await conversations.ListForUserAsync(userId, agentId, q, page, pageSize, ct);

    public async Task<(AgentConversation Conversation, IReadOnlyList<AgentExecution> Turns)> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        var conversation = await OwnedConversationAsync(conversationId, userId, ct);
        var turns = await executionRepo.GetBySessionAsync(conversationId, ct);
        return (conversation, turns);
    }

    public async Task<AgentConversation> UpdateConversationAsync(Guid conversationId, Guid userId, string? title, bool? isPinned, CancellationToken ct = default)
    {
        var conversation = await OwnedConversationAsync(conversationId, userId, ct);
        if (title is not null) conversation.Rename(title);
        if (isPinned.HasValue) conversation.Pin(isPinned.Value);
        conversations.Update(conversation);
        await conversations.SaveAsync(ct);
        return conversation;
    }

    /// <summary>
    /// Supprime un fil et ses tours. Suppression logique : la trace d'audit des
    /// exécutions reste consultable par la console, c'est l'historique de la
    /// personne qui disparaît.
    /// </summary>
    public async Task DeleteConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        var conversation = await OwnedConversationAsync(conversationId, userId, ct);
        foreach (var turn in await executionRepo.GetBySessionAsync(conversationId, ct))
            executionRepo.SoftDelete(turn);
        conversations.SoftDelete(conversation);
        await executionRepo.SaveAsync(ct);
        await conversations.SaveAsync(ct);
    }

    /// <summary>Un fil n'appartient qu'à la personne qui l'a ouvert : pour tout autre, il n'existe pas.</summary>
    private async Task<AgentConversation> OwnedConversationAsync(Guid conversationId, Guid userId, CancellationToken ct)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, ct);
        if (conversation is null || conversation.UserId != userId)
            throw new KeyNotFoundException("Conversation introuvable.");
        return conversation;
    }

    private async Task TouchConversationAsync(AgentExecution execution, CancellationToken ct)
    {
        if (execution.SessionId is not { } sessionId) return;
        var conversation = await conversations.GetByIdAsync(sessionId, ct);
        if (conversation is null) return;
        conversation.RecordTurn(execution);
        conversations.Update(conversation);
        await conversations.SaveAsync(ct);
    }

    /// <summary>
    /// Jeton d'appel de la personne qui reprend, relayé tel quel au runtime.
    ///
    /// C'est sous ce jeton que l'action approuvée sera exécutée, permissions
    /// revérifiées : approuver n'accorde pas un droit qu'on n'avait pas.
    /// </summary>
    private string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MISSING_CALLER_TOKEN");

        return header["Bearer ".Length..].Trim();
    }
}
