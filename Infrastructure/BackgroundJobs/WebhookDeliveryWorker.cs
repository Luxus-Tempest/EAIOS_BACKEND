using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EAIOS.Api.Infrastructure.BackgroundJobs;

/// <summary>Une livraison de webhook à effectuer, telle que mise en file par le service métier.</summary>
public sealed record WebhookDelivery(
    Guid TenantId,
    Guid SubscriptionId,
    string Url,
    string? ProtectedSecret,
    string EventType,
    string PayloadJson,
    Guid EventId,
    int Attempt = 0);

/// <summary>
/// File d'attente en mémoire des livraisons de webhooks.
///
/// Remplace le <c>Task.Run</c> détaché qui capturait des services scopés déjà
/// disposés à l'exécution : ni l'état de livraison ni les erreurs n'étaient
/// alors persistables, et aucune reprise n'était possible.
/// </summary>
public sealed class WebhookDeliveryQueue
{
    // Bounded : si le producteur va plus vite que la livraison, on préfère
    // ralentir l'appelant plutôt que consommer la mémoire sans limite.
    private readonly Channel<WebhookDelivery> _channel =
        Channel.CreateBounded<WebhookDelivery>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

    /// <summary>Met en file sans bloquer. Renvoie false si la file est saturée.</summary>
    public bool TryEnqueue(WebhookDelivery delivery) => _channel.Writer.TryWrite(delivery);

    public IAsyncEnumerable<WebhookDelivery> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Livre les webhooks hors du cycle requête/réponse, avec signature HMAC,
/// réessais à backoff exponentiel et persistance du résultat sur l'abonnement.
/// </summary>
public sealed class WebhookDeliveryWorker(
    WebhookDeliveryQueue queue,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<WebhookDeliveryWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 4;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private IDataProtector Protector => dataProtectionProvider.CreateProtector("WebhookSecrets");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker de livraison des webhooks démarré.");

        await foreach (var delivery in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DeliverAsync(delivery, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur inattendue lors de la livraison du webhook {SubscriptionId}.",
                    delivery.SubscriptionId);
            }
        }

        logger.LogInformation("Worker de livraison des webhooks arrêté.");
    }

    private async Task DeliverAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("WebhookClient");
        client.Timeout = RequestTimeout;

        string? failure = null;
        var delivered = false;

        for (var attempt = 1; attempt <= MaxAttempts && !delivered && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Url)
                {
                    Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("X-Eaios-Event",    delivery.EventType);
                request.Headers.Add("X-Eaios-Event-Id", delivery.EventId.ToString());
                request.Headers.Add("X-Eaios-Attempt",  attempt.ToString());

                // La signature permet au destinataire de vérifier l'origine et l'intégrité.
                if (!string.IsNullOrEmpty(delivery.ProtectedSecret))
                {
                    var rawSecret = TryUnprotect(delivery.ProtectedSecret);
                    if (rawSecret is not null)
                    {
                        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(rawSecret));
                        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(delivery.PayloadJson));
                        request.Headers.Add("X-Eaios-Signature",
                            $"sha256={Convert.ToHexStringLower(hash)}");
                    }
                }

                using var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    delivered = true;
                    failure   = null;
                }
                else
                {
                    failure = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

                    // Une erreur 4xx (hors 408/429) ne se résoudra pas d'elle-même :
                    // réessayer ne ferait que marteler le destinataire.
                    var status = (int)response.StatusCode;
                    if (status is >= 400 and < 500 && status is not 408 and not 429)
                    {
                        logger.LogWarning("Webhook {SubscriptionId} refusé définitivement ({Failure}).",
                            delivery.SubscriptionId, failure);
                        break;
                    }
                }
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = $"Délai dépassé après {RequestTimeout.TotalSeconds:0} s.";
            }
            catch (HttpRequestException ex)
            {
                failure = ex.Message;
            }

            if (!delivered && attempt < MaxAttempts)
            {
                // Backoff exponentiel : 2 s, 4 s, 8 s.
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        await RecordOutcomeAsync(delivery, delivered, failure, ct);

        if (delivered)
            logger.LogInformation("Webhook {EventType} livré à l'abonnement {SubscriptionId}.",
                delivery.EventType, delivery.SubscriptionId);
        else
            logger.LogWarning("Webhook {EventType} non livré à {SubscriptionId} après {Attempts} tentative(s) : {Failure}",
                delivery.EventType, delivery.SubscriptionId, MaxAttempts, failure);
    }

    /// <summary>
    /// Persiste le résultat sur l'abonnement, dans un scope DI propre au worker :
    /// c'est ce qui manquait au dispatch détaché précédent.
    /// </summary>
    private async Task RecordOutcomeAsync(WebhookDelivery delivery, bool delivered, string? failure, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            sp.GetRequiredService<ITenantContext>().SetTenant(delivery.TenantId);
            var db = sp.GetRequiredService<EaiosDbContext>();

            var sub = await db.WebhookSubscriptions
                .FirstOrDefaultAsync(w => w.Id == delivery.SubscriptionId, ct);

            if (sub is null) return;

            sub.LastTriggeredAt = DateTime.UtcNow;

            if (delivered)
            {
                sub.LastError  = null;
                sub.RetryCount = 0;
            }
            else
            {
                sub.LastError  = failure is { Length: > 500 } ? failure[..500] : failure;
                sub.RetryCount++;

                // Un endpoint durablement injoignable est désactivé pour ne pas
                // relancer indéfiniment des livraisons vouées à l'échec.
                if (sub.RetryCount >= 20)
                {
                    sub.IsActive = false;
                    logger.LogWarning("Abonnement webhook {SubscriptionId} désactivé après {Count} échecs consécutifs.",
                        sub.Id, sub.RetryCount);
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impossible d'enregistrer le résultat de livraison du webhook {SubscriptionId}.",
                delivery.SubscriptionId);
        }
    }

    private string? TryUnprotect(string protectedSecret)
    {
        try   { return Protector.Unprotect(protectedSecret); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Secret de webhook illisible — la signature sera omise.");
            return null;
        }
    }
}
