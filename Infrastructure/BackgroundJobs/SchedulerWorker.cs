using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Application.Connector;
using EAIOS.Api.Application.Notification;
using EAIOS.Api.Application.Workflow;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Infrastructure.Email;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Infrastructure.BackgroundJobs;

/// <summary>
/// L'ouvrier du temps.
///
/// <para>
/// Le modèle métier suppose partout que quelque chose se passe quand une
/// échéance tombe — et rien ne se passait. Ce worker balaie chaque minute,
/// tous tenants confondus, et applique dans le contexte de chaque organisation :
/// </para>
/// <list type="bullet">
///   <item>tâches humaines en retard → <b>escaladées</b> à un responsable, puis <b>expirées</b> après le délai de grâce ;</item>
///   <item>instances de workflow échues → <b>délai dépassé</b>, tâches ouvertes closes ;</item>
///   <item>exécutions d'agent restées « en cours » au-delà du raisonnable → <b>délai dépassé</b> ;</item>
///   <item>invitations arrivées à échéance → <b>expirées</b> ;</item>
///   <item>workflows publiés à horaire → <b>lancés</b> à l'heure dite ;</item>
///   <item>résumés de notifications par courriel, à la fréquence choisie par chacun.</item>
/// </list>
/// <para>Chaque geste est notifié à qui de droit ; aucun ne se produit deux fois.</para>
/// </summary>
public sealed class SchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private DateTime _lastDigestPass = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Planificateur démarré.");
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur dans une passe du planificateur.");
            }

            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var graceHours = Math.Max(1, configuration.GetValue("Workflow:EscalationGraceHours", 24));
        var agentTimeoutMinutes = Math.Max(5, configuration.GetValue("AgentRuntime:ExecutionTimeoutMinutes", 30));

        List<Guid> organizations;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
            // Les organisations qui ont quelque chose à échéance : on ne réveille pas les autres.
            var taskOrgs = db.WorkflowTasks.IgnoreQueryFilters()
                .Where(t => !t.IsDeleted && t.DueAt != null && t.DueAt < now
                         && (t.Status == WorkflowTaskStatus.Open || t.Status == WorkflowTaskStatus.InProgress || t.Status == WorkflowTaskStatus.Escalated))
                .Select(t => t.OrganizationId);
            var instanceOrgs = db.WorkflowInstances.IgnoreQueryFilters()
                .Where(i => !i.IsDeleted && i.DueAt != null && i.DueAt < now
                         && (i.Status == WorkflowInstanceStatus.Executing || i.Status == WorkflowInstanceStatus.WaitingForApproval || i.Status == WorkflowInstanceStatus.Paused))
                .Select(i => i.OrganizationId);
            var executionOrgs = db.AgentExecutions.IgnoreQueryFilters()
                .Where(e => !e.IsDeleted && (e.Status == AgentExecutionStatus.Running || e.Status == AgentExecutionStatus.Queued)
                         && e.StartedAt < now.AddMinutes(-agentTimeoutMinutes))
                .Select(e => e.OrganizationId);
            var invitationOrgs = db.Invitations.IgnoreQueryFilters()
                .Where(i => !i.IsDeleted && i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
                .Select(i => i.OrganizationId);
            var scheduledOrgs = db.WorkflowDefinitions.IgnoreQueryFilters()
                .Where(d => !d.IsDeleted && d.Status == WorkflowDefinitionStatus.Published && d.NextRunAt != null && d.NextRunAt <= now)
                .Select(d => d.OrganizationId);

            organizations = await taskOrgs.Concat(instanceOrgs).Concat(executionOrgs).Concat(invitationOrgs).Concat(scheduledOrgs)
                .Distinct().ToListAsync(ct);
        }

        foreach (var organizationId in organizations)
        {
            if (ct.IsCancellationRequested) break;
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(organizationId);

            try
            {
                await EscalateOverdueTasksAsync(sp, organizationId, now, graceHours, ct);
                await TimeOutInstancesAsync(sp, organizationId, now, ct);
                await TimeOutExecutionsAsync(sp, organizationId, now, agentTimeoutMinutes, ct);
                await ExpireInvitationsAsync(sp, now, ct);
                await StartScheduledWorkflowsAsync(sp, organizationId, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Passe de planification en échec pour l'organisation {OrganizationId}.", organizationId);
            }
        }

        // Les résumés par courriel : une passe par heure suffit.
        if (now - _lastDigestPass >= TimeSpan.FromHours(1))
        {
            _lastDigestPass = now;
            await SendDigestsAsync(now, ct);
        }
    }

    // ── Tâches en retard ──────────────────────────────────────────────────────

    private async Task EscalateOverdueTasksAsync(IServiceProvider sp, Guid organizationId, DateTime now, int graceHours, CancellationToken ct)
    {
        var db = sp.GetRequiredService<EaiosDbContext>();
        var dispatcher = sp.GetRequiredService<INotificationDispatcher>();

        var overdue = await db.WorkflowTasks
            .Where(t => t.DueAt != null && t.DueAt < now
                     && (t.Status == WorkflowTaskStatus.Open || t.Status == WorkflowTaskStatus.InProgress || t.Status == WorkflowTaskStatus.Escalated))
            .ToListAsync(ct);

        foreach (var task in overdue)
        {
            // Déjà escaladée et le délai de grâce est passé : la tâche expire.
            if (task.Status == WorkflowTaskStatus.Escalated)
            {
                if (task.EscalatedAt is { } at && at.AddHours(graceHours) < now)
                {
                    task.Expire();
                    foreach (var recipient in new[] { task.AssigneeId, task.EscalatedTo }.Where(r => r.HasValue).Select(r => r!.Value).Distinct())
                        await dispatcher.DispatchAsync(new NotificationRequest(organizationId, recipient, "task.expired",
                            $"Tâche expirée : {task.Title}",
                            "La tâche n'a pas été traitée dans le délai, même après escalade.",
                            "/tasks", "Voir les tâches", NotificationPriority.High), ct);
                    logger.LogInformation("Tâche {TaskId} expirée.", task.Id);
                }
                continue;
            }

            var escalateTo = await ResolveEscalationTargetAsync(db, task, ct);
            if (escalateTo is null)
            {
                logger.LogWarning("Tâche {TaskId} en retard, aucun responsable d'escalade trouvé.", task.Id);
                continue;
            }

            task.Escalate(escalateTo.Value, task.EscalationLevel + 1);

            await dispatcher.DispatchAsync(new NotificationRequest(organizationId, escalateTo.Value, "task.escalated",
                $"Escalade : {task.Title}",
                task.AssigneeId.HasValue
                    ? "La tâche est en retard chez son assignataire : elle vous est remontée."
                    : "La tâche est en retard et n'avait pas d'assignataire : elle vous est remontée.",
                "/tasks", "Traiter", NotificationPriority.High), ct);

            if (task.AssigneeId is { } assignee && assignee != escalateTo.Value)
                await dispatcher.DispatchAsync(new NotificationRequest(organizationId, assignee, "task.escalated",
                    $"Tâche en retard : {task.Title}",
                    "L'échéance est dépassée : la tâche a été remontée à un responsable.",
                    "/tasks", "Voir", NotificationPriority.High), ct);

            logger.LogInformation("Tâche {TaskId} escaladée vers {EscalatedTo}.", task.Id, escalateTo);
        }

        if (overdue.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// À qui remonter : le propriétaire du workflow s'il y en a un, sinon le
    /// premier administrateur d'organisation. Jamais l'assignataire lui-même.
    /// </summary>
    private static async Task<Guid?> ResolveEscalationTargetAsync(EaiosDbContext db, WorkflowTask task, CancellationToken ct)
    {
        if (task.InstanceId is { } instanceId)
        {
            var owner = await (from i in db.WorkflowInstances
                               join d in db.WorkflowDefinitions on i.DefinitionId equals d.Id
                               where i.Id == instanceId
                               select (Guid?)d.OwnerId).FirstOrDefaultAsync(ct);
            if (owner.HasValue && owner != task.AssigneeId) return owner;
        }

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == Domain.AccessControl.SystemRoles.OrgAdmin, ct);
        if (adminRole is null) return null;

        var admins = await db.UserRoles
            .Where(ur => ur.RoleId == adminRole.Id && (ur.ExpiresAt == null || ur.ExpiresAt > DateTime.UtcNow))
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        return admins.FirstOrDefault(a => a != task.AssigneeId) is var candidate && candidate != Guid.Empty ? candidate : null;
    }

    // ── Instances échues ──────────────────────────────────────────────────────

    private async Task TimeOutInstancesAsync(IServiceProvider sp, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var db = sp.GetRequiredService<EaiosDbContext>();
        var dispatcher = sp.GetRequiredService<INotificationDispatcher>();

        var expired = await db.WorkflowInstances
            .Where(i => i.DueAt != null && i.DueAt < now
                     && (i.Status == WorkflowInstanceStatus.Executing || i.Status == WorkflowInstanceStatus.WaitingForApproval || i.Status == WorkflowInstanceStatus.Paused))
            .ToListAsync(ct);

        foreach (var instance in expired)
        {
            instance.TimeOut();
            var open = await db.WorkflowTasks
                .Where(t => t.InstanceId == instance.Id
                         && (t.Status == WorkflowTaskStatus.Open || t.Status == WorkflowTaskStatus.InProgress || t.Status == WorkflowTaskStatus.Escalated))
                .ToListAsync(ct);
            foreach (var task in open) task.Cancel();

            if (instance.TriggeredBy is { } starter)
                await dispatcher.DispatchAsync(new NotificationRequest(organizationId, starter, "workflow.timed_out",
                    "Workflow arrivé à échéance",
                    $"L'instance n'a pas abouti avant son échéance ; {open.Count} tâche(s) ouverte(s) close(s).",
                    $"/workflows/{instance.DefinitionId}", "Voir le workflow", NotificationPriority.High), ct);

            logger.LogInformation("Instance {InstanceId} : délai dépassé.", instance.Id);
        }

        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }

    // ── Exécutions d'agent figées ─────────────────────────────────────────────

    private async Task TimeOutExecutionsAsync(IServiceProvider sp, Guid organizationId, DateTime now, int timeoutMinutes, CancellationToken ct)
    {
        var db = sp.GetRequiredService<EaiosDbContext>();
        var dispatcher = sp.GetRequiredService<INotificationDispatcher>();

        var stale = await db.AgentExecutions
            .Where(e => (e.Status == AgentExecutionStatus.Running || e.Status == AgentExecutionStatus.Queued)
                     && e.StartedAt < now.AddMinutes(-timeoutMinutes))
            .ToListAsync(ct);

        foreach (var execution in stale)
        {
            execution.TimeOut();
            if (execution.UserId is { } user)
                await dispatcher.DispatchAsync(new NotificationRequest(organizationId, user, "agent.timed_out",
                    "Exécution d'agent interrompue",
                    $"L'exécution est restée en cours plus de {timeoutMinutes} minutes : elle est marquée « délai dépassé ».",
                    $"/executions/{execution.Id}", "Voir l'exécution", NotificationPriority.Normal), ct);
        }

        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }

    // ── Invitations ───────────────────────────────────────────────────────────

    private static async Task ExpireInvitationsAsync(IServiceProvider sp, DateTime now, CancellationToken ct)
    {
        var db = sp.GetRequiredService<EaiosDbContext>();
        var pending = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
            .ToListAsync(ct);
        foreach (var invitation in pending) invitation.Expire();
        if (pending.Count > 0) await db.SaveChangesAsync(ct);
    }

    // ── Workflows à horaire ───────────────────────────────────────────────────

    private async Task StartScheduledWorkflowsAsync(IServiceProvider sp, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var db = sp.GetRequiredService<EaiosDbContext>();
        var workflows = sp.GetRequiredService<IWorkflowService>();
        var dispatcher = sp.GetRequiredService<INotificationDispatcher>();

        var due = await db.WorkflowDefinitions
            .Where(d => d.Status == WorkflowDefinitionStatus.Published && d.NextRunAt != null && d.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var definition in due)
        {
            // L'horaire avance d'abord : un échec de lancement ne doit pas
            // relancer le même workflow à chaque minute.
            var next = CronSchedule.TryParse(definition.ScheduleCron, out var schedule) && schedule is not null
                ? schedule.GetNextOccurrence(now.AddMinutes(1))
                : null;
            definition.SetSchedule(definition.ScheduleCron, next);
            await db.SaveChangesAsync(ct);

            try
            {
                await workflows.StartInstanceAsync(organizationId, definition.Id, WorkflowTriggerType.Scheduled,
                    definition.OwnerId, null, null, ct);
                logger.LogInformation("Workflow planifié « {Name} » lancé.", definition.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lancement planifié de « {Name} » en échec.", definition.Name);
                await dispatcher.DispatchAsync(new NotificationRequest(organizationId, definition.OwnerId, "workflow.schedule_failed",
                    $"Lancement planifié en échec : {definition.Name}", ex.Message,
                    $"/workflows/{definition.Id}", "Voir le workflow", NotificationPriority.High), ct);
            }
        }
    }

    // ── Résumés par courriel ──────────────────────────────────────────────────

    /// <summary>
    /// Un courriel par personne et par période, listant ce qu'elle n'a pas lu.
    /// La fenêtre suit la fréquence choisie ; la passe horaire n'envoie le
    /// quotidien qu'après 7 h UTC et l'hebdomadaire que le lundi.
    /// </summary>
    private async Task SendDigestsAsync(DateTime now, CancellationToken ct)
    {
        List<(Guid UserId, Guid OrganizationId)> users;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EaiosDbContext>();
            users = await db.Users.IgnoreQueryFilters()
                .Where(u => !u.IsDeleted && u.Status == UserStatus.Active
                         && u.NotificationPreferences != null && u.NotificationPreferences.Contains("DigestFrequency"))
                .Select(u => new ValueTuple<Guid, Guid>(u.Id, u.OrganizationId))
                .ToListAsync(ct);
        }

        foreach (var (userId, organizationId) in users)
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(organizationId);
            var db = sp.GetRequiredService<EaiosDbContext>();
            var notifications = sp.GetRequiredService<INotificationService>();
            var mail = sp.GetRequiredService<IEmailService>();

            NotificationPreferencesDto prefs;
            try { prefs = await notifications.GetPreferencesAsync(userId, ct); }
            catch (KeyNotFoundException) { continue; }

            if (!prefs.EmailEnabled) continue;
            var window = prefs.DigestFrequency?.ToLowerInvariant() switch
            {
                "hourly" => TimeSpan.FromHours(1),
                "daily" when now.Hour == 7 => TimeSpan.FromDays(1),
                "weekly" when now.DayOfWeek == DayOfWeek.Monday && now.Hour == 7 => TimeSpan.FromDays(7),
                _ => (TimeSpan?)null,
            };
            if (window is null) continue;

            var since = now - window.Value;
            var unread = await db.Notifications
                .Where(n => n.RecipientId == userId && !n.IsRead && n.CreatedAt >= since)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .ToListAsync(ct);
            if (unread.Count == 0) continue;

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) continue;

            var body = string.Join("\n", unread.Select(n => $"• {n.Title}{(string.IsNullOrWhiteSpace(n.Body) ? "" : " — " + n.Body)}"));
            try
            {
                await mail.SendNotificationAsync(user.Email,
                    $"EAIOS — {unread.Count} notification{(unread.Count > 1 ? "s" : "")} non lue{(unread.Count > 1 ? "s" : "")}",
                    body, (configuration["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/') + "/notifications",
                    "Ouvrir les notifications", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Résumé de notifications non envoyé à {Email}.", user.Email);
            }
        }
    }
}
