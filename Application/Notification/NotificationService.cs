using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using System.Text.Json;

namespace EAIOS.Api.Application.Notification;

public sealed class NotificationService(
    INotificationRepository notifRepo,
    IUserRepository userRepo) : INotificationService
{
    public async Task<PagedResult<Domain.Notification.Notification>> ListAsync(Guid recipientId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        return await notifRepo.GetByRecipientAsync(recipientId, unreadOnly, page, pageSize, ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid recipientId, CancellationToken ct = default)
    {
        return await notifRepo.GetUnreadCountAsync(recipientId, ct);
    }

    public async Task MarkReadAsync(Guid id, Guid recipientId, CancellationToken ct = default)
    {
        var notif = await notifRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Notification introuvable.");
        if (notif.RecipientId != recipientId) throw new KeyNotFoundException("Notification introuvable pour cet utilisateur.");

        notif.MarkRead();
        notifRepo.Update(notif);
        await notifRepo.SaveAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid recipientId, CancellationToken ct = default)
    {
        await notifRepo.MarkAllReadAsync(recipientId, ct);
        await notifRepo.SaveAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid recipientId, CancellationToken ct = default)
    {
        var notif = await notifRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Notification introuvable.");
        if (notif.RecipientId != recipientId) throw new KeyNotFoundException("Notification introuvable pour cet utilisateur.");

        notifRepo.SoftDelete(notif);
        await notifRepo.SaveAsync(ct);
    }

    // ── Preferences ───────────────────────────────────────────────────────────
    // Stockees en JSON sur User.NotificationPreferences : la colonne existait
    // deja mais n'etait exposee par aucun endpoint.

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Valeurs appliquees tant que l'utilisateur n'a rien personnalise.</summary>
    private static NotificationPreferencesDto Defaults => new(
        InAppEnabled:     true,
        EmailEnabled:     true,
        SmsEnabled:       false,
        PushEnabled:      false,
        DigestFrequency:  "daily",
        ChannelOverrides: []);

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        return Deserialize(user.NotificationPreferences);
    }

    public async Task<NotificationPreferencesDto> UpdatePreferencesAsync(
        Guid userId, UpdatePreferencesRequest request, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        var current = Deserialize(user.NotificationPreferences);

        var frequency = request.DigestFrequency?.Trim().ToLowerInvariant();
        if (frequency is not null && frequency is not ("none" or "realtime" or "hourly" or "daily" or "weekly"))
            throw new ArgumentException(
                "Frequence de resume invalide. Valeurs acceptees : none, realtime, hourly, daily, weekly.");

        // Mise a jour partielle : un champ absent conserve sa valeur actuelle.
        var updated = new NotificationPreferencesDto(
            InAppEnabled:     request.InAppEnabled     ?? current.InAppEnabled,
            EmailEnabled:     request.EmailEnabled     ?? current.EmailEnabled,
            SmsEnabled:       request.SmsEnabled       ?? current.SmsEnabled,
            PushEnabled:      request.PushEnabled      ?? current.PushEnabled,
            DigestFrequency:  frequency               ?? current.DigestFrequency,
            ChannelOverrides: request.ChannelOverrides ?? current.ChannelOverrides);

        user.SetNotificationPreferences(JsonSerializer.Serialize(updated));
        userRepo.Update(user);
        await userRepo.SaveAsync(ct);

        return updated;
    }

    private static NotificationPreferencesDto Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults;

        try
        {
            return JsonSerializer.Deserialize<NotificationPreferencesDto>(json, JsonOptions) ?? Defaults;
        }
        catch (JsonException)
        {
            // Preferences corrompues : on repart des valeurs par defaut plutot
            // que de bloquer l'acces aux notifications.
            return Defaults;
        }
    }
}

public sealed class NotificationTemplateService(
    INotificationTemplateRepository templateRepo) : INotificationTemplateService
{
    public async Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken ct = default)
    {
        return await templateRepo.GetAllAsync(ct);
    }

    public async Task<NotificationTemplate> GetTemplateAsync(Guid id, CancellationToken ct = default)
    {
        return await templateRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Template introuvable.");
    }

    public async Task<NotificationTemplate> CreateTemplateAsync(Guid tenantId, string eventType, NotificationChannel channel, string language, string subjectTemplate, string bodyTemplate, Guid actorId, bool isSystem, CancellationToken ct = default)
    {
        var template = NotificationTemplate.Create(tenantId, eventType, channel, language, subjectTemplate, bodyTemplate, actorId, isSystem);
        
        await templateRepo.AddAsync(template, ct);
        await templateRepo.SaveAsync(ct);
        
        return template;
    }

    public async Task<NotificationTemplate> UpdateTemplateAsync(Guid id, string? subjectTemplate, string? bodyTemplate, bool? isActive, CancellationToken ct = default)
    {
        var template = await templateRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Template introuvable.");
        
        template.Update(subjectTemplate, bodyTemplate, isActive);
        
        templateRepo.Update(template);
        await templateRepo.SaveAsync(ct);
        
        return template;
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await templateRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Template introuvable.");
        
        templateRepo.SoftDelete(template);
        await templateRepo.SaveAsync(ct);
    }
}
