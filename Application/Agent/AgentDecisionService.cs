using System.Text.Json;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Application.Agent;

/// <summary>
/// Transforme un arrêt d'agent en tâche humaine.
///
/// <para>
/// C'est le chaînon qui relie les trois surfaces du design : le bandeau
/// « L'agent demande une décision » dans la conversation, la ligne
/// <c>WaitingForInput</c> dans les exécutions, et la file « Tâches ». Elles
/// montrent le <b>même</b> objet — aucune ne fabrique son propre état.
/// </para>
/// <para>
/// Le runtime ne crée jamais cette tâche : il signale, le backend inscrit.
/// </para>
/// </summary>
public interface IAgentDecisionService
{
    Task<WorkflowTask?> OpenAsync(
        AgentExecution execution, RuntimeDecisionRequest decision, CancellationToken ct = default);

    /// <summary>
    /// Clôt la tâche de décision d'une exécution : la personne a répondu depuis
    /// la conversation ou la fiche d'exécution plutôt que depuis la file, ou a
    /// poursuivi la conversation sans trancher. Sans cela, la file « Tâches »
    /// garderait ouverte une décision déjà prise — ou que plus personne n'attend.
    /// </summary>
    Task CloseAsync(AgentExecution execution, Guid actorId, string decision, string? comment, CancellationToken ct = default);
}

public sealed class AgentDecisionService(
    EaiosDbContext db,
    ILogger<AgentDecisionService> logger,
    EAIOS.Api.Application.Notification.INotificationDispatcher dispatcher,
    EAIOS.Api.Application.Realtime.IRealtimeEventService realtime) : IAgentDecisionService
{
    public async Task<WorkflowTask?> OpenAsync(
        AgentExecution execution, RuntimeDecisionRequest decision, CancellationToken ct = default)
    {
        // Un arrêt peut survenir plusieurs fois sur le même fil : à la reprise,
        // l'agent a le droit de redemander. Rouvrir une tâche déjà ouverte pour
        // la même exécution ferait deux lignes dans la file pour une seule
        // décision en attente.
        var alreadyOpen = await db.WorkflowTasks
            .AnyAsync(t => t.AgentExecutionId == execution.Id
                        && t.Status == WorkflowTaskStatus.Open, ct);

        if (alreadyOpen)
        {
            logger.LogDebug("Exécution {ExecutionId} : une tâche est déjà ouverte.", execution.Id);
            return null;
        }

        var task = WorkflowTask.ForAgentDecision(
            organizationId:   execution.OrganizationId,
            agentExecutionId: execution.Id,
            taskType:         decision.Kind,
            title:            decision.Title,
            instructions:     BuildInstructions(decision),
            // Celui qui a lancé l'exécution décide. Une réassignation reste
            // possible ensuite — `WorkflowTask.Reassign` existe pour cela.
            assigneeId:       execution.UserId,
            // La charge utile complète, telle que le runtime l'a produite :
            // l'écran d'approbation doit montrer de quoi juger sans rouvrir la
            // conversation.
            formDataJson:     JsonSerializer.Serialize(decision));

        await db.WorkflowTasks.AddAsync(task, ct);
        await db.SaveChangesAsync(ct);

        if (execution.UserId is { } recipient)
        {
            // Une exécution en attente bloque : elle ne consomme plus de budget
            // mais elle n'avance pas non plus. Le point d'émission applique les
            // préférences et pousse la cloche en direct.
            await dispatcher.DispatchAsync(new EAIOS.Api.Application.Notification.NotificationRequest(
                execution.OrganizationId, recipient, "agent.decision_required",
                decision.Title, decision.Question,
                $"/executions/{execution.Id}", "Décider", NotificationPriority.High,
                new { taskId = task.Id, executionId = execution.Id }), ct);
        }

        await realtime.PublishToTenantAsync(execution.OrganizationId, "workflow.task", new { taskId = task.Id, executionId = execution.Id });

        logger.LogInformation(
            "Exécution {ExecutionId} en attente : tâche {TaskId} ouverte ({Tool}).",
            execution.Id, task.Id, decision.ProposedTool ?? "sans outil");

        return task;
    }

    public async Task CloseAsync(AgentExecution execution, Guid actorId, string decision, string? comment, CancellationToken ct = default)
    {
        var open = await db.WorkflowTasks
            .Where(t => t.AgentExecutionId == execution.Id && t.Status == WorkflowTaskStatus.Open)
            .ToListAsync(ct);

        if (open.Count == 0) return;

        // Une décision prise ailleurs se journalise ici comme si elle l'avait été
        // dans la file : qui, quand, quoi.
        foreach (var task in open)
            task.Complete(actorId, decision, comment, null);

        await db.SaveChangesAsync(ct);
        await realtime.PublishToTenantAsync(execution.OrganizationId, "workflow.task", new { executionId = execution.Id });

        logger.LogInformation(
            "Exécution {ExecutionId} : {Count} tâche(s) de décision close(s) ({Decision}).",
            execution.Id, open.Count, decision);
    }

    /// <summary>
    /// Ce que la personne doit lire pour décider : la question, ce que l'agent
    /// ferait, et pourquoi il s'arrête.
    /// </summary>
    private static string BuildInstructions(RuntimeDecisionRequest decision)
    {
        var parts = new List<string> { decision.Question };

        if (!string.IsNullOrWhiteSpace(decision.Rationale))
            parts.Add(decision.Rationale);

        if (!string.IsNullOrWhiteSpace(decision.ProposedTool))
            parts.Add($"Action envisagée : {decision.ProposedTool}.");

        if (decision.Options.Count > 0)
            parts.Add("Issues possibles : " + string.Join(" · ", decision.Options.Select(o => o.Label)) + ".");

        return string.Join("\n\n", parts);
    }
}
