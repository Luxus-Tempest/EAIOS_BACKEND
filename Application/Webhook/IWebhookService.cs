using EAIOS.Api.Application.Common.Models;

namespace EAIOS.Api.Application.Webhook;

public sealed record WebhookSubscriptionDto(
    Guid Id,
    string Name,
    string Url,
    string SubscribedEvents,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastTriggeredAt,
    string? LastError);

public sealed record CreateWebhookRequest(
    string Name,
    string Url,
    string? Secret,
    string SubscribedEvents);

public sealed record UpdateWebhookRequest(
    string? Name,
    string? Url,
    string? Secret,
    string? SubscribedEvents,
    bool? IsActive);

public interface IWebhookService
{
    Task<IReadOnlyList<WebhookSubscriptionDto>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> GetSubscriptionAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> CreateSubscriptionAsync(Guid tenantId, CreateWebhookRequest req, Guid actorId, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> UpdateSubscriptionAsync(Guid id, Guid tenantId, UpdateWebhookRequest req, Guid actorId, CancellationToken ct = default);
    Task DeleteSubscriptionAsync(Guid id, Guid tenantId, Guid actorId, CancellationToken ct = default);
    Task<bool> TestSubscriptionAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task PublishEventAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default);
}
