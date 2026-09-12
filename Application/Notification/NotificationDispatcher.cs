using System.Text.Json;
using EAIOS.Api.Application.Realtime;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Infrastructure.Email;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Application.Notification;

/// <summary>Ce que l'on veut dire à quelqu'un, indépendamment du canal.</summary>
public sealed record NotificationRequest(
    Guid OrganizationId,
    Guid RecipientId,
    /// <summary>Type d'événement : <c>document.shared</c>, <c>task.assigned</c>, <c>agent.decision_required</c>…</summary>
    string EventType,
    string Title,
    string? Body = null,
    string? ActionUrl = null,
    string? ActionLabel = null,
    NotificationPriority Priority = NotificationPriority.Normal,
    object? Data = null);

/// <summary>
/// Ce qu'une personne — ou un agent, après décision humaine — demande à faire
/// parvenir à un collègue. Sert l'outil <c>notify_user</c> du runtime et le
/// geste « Prévenir » de l'interface.
/// </summary>
public sealed record SendNotificationRequest(
    Guid RecipientId,
    string Title,
    string? Body = null,
    string? ActionUrl = null,
    string? ActionLabel = null,
    NotificationPriority Priority = NotificationPriority.Normal);

/// <summary>
/// Le point d'émission unique des notifications.
///
/// <para>
/// Jusqu'ici, une seule notification était jamais produite dans tout le produit
/// (la décision demandée par un agent), et ni les préférences ni les modèles
/// n'étaient lus par personne. Ce service est appelé aux moments qui comptent —
/// partage, assignation, décision, rapport prêt, conservation légale, échéance —
/// et fait trois choses, selon les préférences de la personne : l'inscrit dans
/// sa boîte, la pousse sur le flux temps réel, et l'envoie par courriel.
/// </para>
/// <para>
/// Un modèle de notification (<see cref="NotificationTemplate"/>) actif pour ce
/// type et ce canal habille le message : <c>{{title}}</c>, <c>{{body}}</c>,
/// <c>{{actionUrl}}</c>, <c>{{actionLabel}}</c>. Sans modèle, le message brut suffit.
/// </para>
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationRequest request, CancellationToken ct = default);
    Task DispatchManyAsync(IEnumerable<Guid> recipientIds, NotificationRequest template, CancellationToken ct = default);
}

public sealed class NotificationDispatcher(
    EaiosDbContext db,
    INotificationService notifications,
    IRealtimeEventService realtime,
    IEmailService email,
    IConfiguration configuration,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task DispatchManyAsync(IEnumerable<Guid> recipientIds, NotificationRequest template, CancellationToken ct = default)
    {
        foreach (var recipient in recipientIds.Distinct())
            await DispatchAsync(template with { RecipientId = recipient }, ct);
    }

    public async Task DispatchAsync(NotificationRequest request, CancellationToken ct = default)
    {
        NotificationPreferencesDto prefs;
        try
        {
            prefs = await notifications.GetPreferencesAsync(request.RecipientId, ct);
        }
        catch (KeyNotFoundException)
        {
            logger.LogWarning("Notification {Type} : destinataire {RecipientId} introuvable.", request.EventType, request.RecipientId);
            return;
        }

        var dataJson = request.Data is null ? null : JsonSerializer.Serialize(request.Data);
        var templates = await db.NotificationTemplates
            .Where(t => t.EventType == request.EventType && t.IsActive)
            .ToListAsync(ct);

        // ── Boîte de réception + flux temps réel ──────────────────────────────
        if (Enabled(prefs, request.EventType, NotificationChannel.InApp, prefs.InAppEnabled))
        {
            var (title, body) = Render(templates, NotificationChannel.InApp, request);
            var notification = Domain.Notification.Notification.Create(
                request.OrganizationId, request.RecipientId, NotificationChannel.InApp,
                request.EventType, title, body, request.Priority, request.ActionUrl, request.ActionLabel, dataJson);

            await db.Notifications.AddAsync(notification, ct);
            await db.SaveChangesAsync(ct);

            // Le canal existait, personne n'y écrivait : la cloche vit enfin.
            await realtime.PublishToUserAsync(request.OrganizationId, request.RecipientId, "notification", new
            {
                id = notification.Id,
                type = request.EventType,
                title,
                body,
                actionUrl = request.ActionUrl,
                actionLabel = request.ActionLabel,
                priority = request.Priority.ToString(),
                createdAt = notification.CreatedAt,
            });
        }

        // ── Courriel ──────────────────────────────────────────────────────────
        // Immédiat quand la personne le demande (« realtime ») ; sinon le
        // résumé périodique du planificateur s'en charge (voir SchedulerWorker).
        var immediate = string.Equals(prefs.DigestFrequency, "realtime", StringComparison.OrdinalIgnoreCase);
        if (immediate && Enabled(prefs, request.EventType, NotificationChannel.Email, prefs.EmailEnabled))
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.RecipientId, ct);
            if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
            {
                var (subject, body) = Render(templates, NotificationChannel.Email, request);
                var baseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
                var link = request.ActionUrl is null ? null : baseUrl.TrimEnd('/') + request.ActionUrl;
                try
                {
                    await email.SendNotificationAsync(user.Email, subject, body, link, request.ActionLabel, ct);
                }
                catch (Exception ex)
                {
                    // Un courriel qui échoue ne doit pas faire échouer l'opération métier.
                    logger.LogWarning(ex, "Courriel de notification {Type} non envoyé à {Email}.", request.EventType, user.Email);
                }
            }
        }
    }

    /// <summary>Le canal est actif si le réglage global l'est, sauf surcharge par type d'événement.</summary>
    private static bool Enabled(NotificationPreferencesDto prefs, string eventType, NotificationChannel channel, bool global)
    {
        var overrideRule = prefs.ChannelOverrides.FirstOrDefault(o =>
            string.Equals(o.EventType, eventType, StringComparison.OrdinalIgnoreCase) && o.Channel == channel);
        return overrideRule?.Enabled ?? global;
    }

    private static (string Title, string? Body) Render(IReadOnlyList<NotificationTemplate> templates, NotificationChannel channel, NotificationRequest request)
    {
        var template = templates.FirstOrDefault(t => t.Channel == channel)
                    ?? templates.FirstOrDefault(t => t.Channel == NotificationChannel.InApp);
        if (template is null) return (request.Title, request.Body);

        string Fill(string text) => text
            .Replace("{{title}}", request.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{{body}}", request.Body ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{actionUrl}}", request.ActionUrl ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{actionLabel}}", request.ActionLabel ?? "", StringComparison.OrdinalIgnoreCase);

        var title = string.IsNullOrWhiteSpace(template.SubjectTemplate) ? request.Title : Fill(template.SubjectTemplate);
        var body  = string.IsNullOrWhiteSpace(template.BodyTemplate) ? request.Body : Fill(template.BodyTemplate);
        return (title, body);
    }
}
