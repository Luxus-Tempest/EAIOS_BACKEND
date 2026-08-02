namespace EAIOS.Api.Application.Realtime;

public interface IRealtimeEventService
{
    // Ajoute un client à la liste de diffusion
    Task SubscribeAsync(Guid tenantId, Guid userId, Func<string, Task> onMessage, CancellationToken ct);
    
    // Publie un événement à un utilisateur précis (ex: notification lue)
    Task PublishToUserAsync(Guid tenantId, Guid userId, string eventType, object payload);
    
    // Publie un événement à tous les membres d'un tenant (ex: modification de configuration globale)
    Task PublishToTenantAsync(Guid tenantId, string eventType, object payload);
}
