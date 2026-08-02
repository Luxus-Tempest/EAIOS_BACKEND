using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

namespace EAIOS.Api.Application.Notification;

public sealed class NotificationService(
    INotificationRepository notifRepo) : INotificationService
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
