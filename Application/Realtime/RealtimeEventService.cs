using System.Collections.Concurrent;
using System.Text.Json;

namespace EAIOS.Api.Application.Realtime;

public sealed class RealtimeEventService : IRealtimeEventService, IDisposable
{
    // Dictionnaire de connexions actives. Clé : Guid (identifiant unique de connexion)
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();

    public async Task SubscribeAsync(Guid tenantId, Guid userId, Func<string, Task> onMessage, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid();
        var client = new ClientConnection(tenantId, userId, onMessage);
        
        if (_clients.TryAdd(connectionId, client))
        {
            try
            {
                // Maintient la connexion ouverte jusqu'à l'annulation (ex: fermeture du navigateur)
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (TaskCanceledException)
            {
                // Ignoré, déconnexion normale
            }
            finally
            {
                _clients.TryRemove(connectionId, out _);
            }
        }
    }

    public async Task PublishToUserAsync(Guid tenantId, Guid userId, string eventType, object payload)
    {
        var message = FormatSseMessage(eventType, payload);
        
        var userClients = _clients.Values.Where(c => c.TenantId == tenantId && c.UserId == userId);
        
        foreach (var client in userClients)
        {
            try
            {
                await client.OnMessage(message);
            }
            catch
            {
                // Log erreur, la connexion sera probablement fermée par le client
            }
        }
    }

    public async Task PublishToTenantAsync(Guid tenantId, string eventType, object payload)
    {
        var message = FormatSseMessage(eventType, payload);
        
        var tenantClients = _clients.Values.Where(c => c.TenantId == tenantId);
        
        foreach (var client in tenantClients)
        {
            try
            {
                await client.OnMessage(message);
            }
            catch
            {
                // Ignore failure for one client
            }
        }
    }

    private static string FormatSseMessage(string eventType, object payload)
    {
        var data = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return $"event: {eventType}\ndata: {data}\n\n";
    }

    public void Dispose()
    {
        _clients.Clear();
    }

    private sealed record ClientConnection(Guid TenantId, Guid UserId, Func<string, Task> OnMessage);
}
