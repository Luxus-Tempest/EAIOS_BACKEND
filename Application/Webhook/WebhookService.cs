using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using EAIOS.Api.Domain.Webhook;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;

namespace EAIOS.Api.Application.Webhook;

public sealed class WebhookService(
    IWebhookSubscriptionRepository webhookRepo,
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtectionProvider) : IWebhookService
{
    private IDataProtector Protector => dataProtectionProvider.CreateProtector("WebhookSecrets");

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subs = await webhookRepo.GetActiveSubscriptionsAsync(tenantId, ct);
        return subs.Select(Map).ToList();
    }

    public async Task<WebhookSubscriptionDto> GetSubscriptionAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var sub = await GetOwnedAsync(id, tenantId, ct);
        return Map(sub);
    }

    public async Task<WebhookSubscriptionDto> CreateSubscriptionAsync(Guid tenantId, CreateWebhookRequest req, Guid actorId, CancellationToken ct = default)
    {
        var sub = new WebhookSubscription
        {
            Id = Guid.CreateVersion7(),
            Name = req.Name,
            Url = req.Url,
            Secret = string.IsNullOrEmpty(req.Secret) ? null : Protector.Protect(req.Secret),
            SubscribedEvents = req.SubscribedEvents,
            IsActive = true
        };
        sub.SetOrganizationId(tenantId);
        sub.SetCreated(actorId);
        
        await webhookRepo.AddAsync(sub, ct);
        await webhookRepo.SaveAsync(ct);
        
        return Map(sub);
    }

    public async Task<WebhookSubscriptionDto> UpdateSubscriptionAsync(Guid id, Guid tenantId, UpdateWebhookRequest req, Guid actorId, CancellationToken ct = default)
    {
        var sub = await GetOwnedAsync(id, tenantId, ct);
        
        if (req.Name != null) sub.Name = req.Name;
        if (req.Url != null) sub.Url = req.Url;
        if (req.Secret != null) sub.Secret = string.IsNullOrEmpty(req.Secret) ? null : Protector.Protect(req.Secret);
        if (req.SubscribedEvents != null) sub.SubscribedEvents = req.SubscribedEvents;
        if (req.IsActive.HasValue) sub.IsActive = req.IsActive.Value;
        
        webhookRepo.Update(sub);
        await webhookRepo.SaveAsync(ct);
        
        return Map(sub);
    }

    public async Task DeleteSubscriptionAsync(Guid id, Guid tenantId, Guid actorId, CancellationToken ct = default)
    {
        var sub = await GetOwnedAsync(id, tenantId, ct);
        webhookRepo.SoftDelete(sub);
        await webhookRepo.SaveAsync(ct);
    }

    public async Task<bool> TestSubscriptionAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var sub = await GetOwnedAsync(id, tenantId, ct);
        
        var payload = new
        {
            EventId = Guid.CreateVersion7(),
            EventType = "ping",
            Timestamp = DateTime.UtcNow,
            Data = new { Message = "Test Webhook Ping" }
        };

        var httpClient = httpClientFactory.CreateClient("WebhookClient");
        // Timeout court pour le test
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        
        try
        {
            var contentString = JsonSerializer.Serialize(payload);
            var content = new StringContent(contentString, System.Text.Encoding.UTF8, "application/json");
            
            var request = new HttpRequestMessage(HttpMethod.Post, sub.Url) { Content = content };
            
            if (!string.IsNullOrEmpty(sub.Secret))
            {
                var rawSecret = Protector.Unprotect(sub.Secret);
                using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(rawSecret));
                var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(contentString));
                request.Headers.Add("X-Eaios-Signature", $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}");
            }
            
            var response = await httpClient.SendAsync(request, ct);
            
            sub.LastTriggeredAt = DateTime.UtcNow;
            
            if (!response.IsSuccessStatusCode)
            {
                sub.LastError = $"HTTP {response.StatusCode}";
                sub.RetryCount++;
            }
            else
            {
                sub.LastError = null;
                sub.RetryCount = 0;
            }
            
            webhookRepo.Update(sub);
            await webhookRepo.SaveAsync(ct);
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            sub.LastTriggeredAt = DateTime.UtcNow;
            sub.LastError = ex.Message;
            sub.RetryCount++;
            webhookRepo.Update(sub);
            await webhookRepo.SaveAsync(ct);
            
            return false;
        }
    }

    public async Task PublishEventAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default)
    {
        var subs = await webhookRepo.GetActiveSubscriptionsAsync(tenantId, ct);
        
        // Filtrer les abonnements qui écoutent cet événement
        var interestedSubs = subs.Where(s => 
            s.SubscribedEvents == "*" || 
            s.SubscribedEvents.Split(',').Select(e => e.Trim()).Contains(eventType)
        ).ToList();

        if (!interestedSubs.Any()) return;

        // Fire-and-forget: dispatch asynchrone réel
        // Dans une vraie prod, on publierait dans Kafka, RabbitMQ ou Hangfire
        // Ici on simule une background task (dead-letter queue / retries sont gérés par le background job en prod)
        _ = Task.Run(async () =>
        {
            using var httpClient = httpClientFactory.CreateClient("WebhookClient");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var evt = new
            {
                EventId = Guid.CreateVersion7(),
                EventType = eventType,
                Timestamp = DateTime.UtcNow,
                Data = payload
            };
            var contentString = JsonSerializer.Serialize(evt);

            foreach (var sub in interestedSubs)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, sub.Url)
                    {
                        Content = new StringContent(contentString, System.Text.Encoding.UTF8, "application/json")
                    };
                    
                    if (!string.IsNullOrEmpty(sub.Secret))
                    {
                        var rawSecret = Protector.Unprotect(sub.Secret);
                        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(rawSecret));
                        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(contentString));
                        request.Headers.Add("X-Eaios-Signature", $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}");
                    }
                    
                    var response = await httpClient.SendAsync(request);
                    // Mettre à jour LastTriggeredAt etc (via DbContext dans un scope)
                }
                catch
                {
                    // Log error, retry policy, dead letter queue
                }
            }
        });
    }

    private async Task<WebhookSubscription> GetOwnedAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var sub = await webhookRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Webhook introuvable.");
        if (sub.OrganizationId != tenantId) throw new KeyNotFoundException("Webhook introuvable.");
        return sub;
    }

    private static WebhookSubscriptionDto Map(WebhookSubscription s) => new(
        s.Id, s.Name, s.Url, s.SubscribedEvents, s.IsActive, s.CreatedAt, s.LastTriggeredAt, s.LastError);
}
