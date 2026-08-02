using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Webhook;

public sealed class WebhookSubscription : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    
    // Comma-separated list of event types, e.g., "document.created,knowledge.published"
    public string SubscribedEvents { get; set; } = "*";
    
    public bool IsActive { get; set; } = true;
    public DateTime? LastTriggeredAt { get; set; }
    public string? LastError { get; set; }
    public int RetryCount { get; set; } = 0;
}
