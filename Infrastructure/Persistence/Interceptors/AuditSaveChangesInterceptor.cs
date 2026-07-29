using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Shared.Primitives;
using EAIOS.Api.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace EAIOS.Api.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Intercepts SaveChanges to automatically emit AuditEvents for all data mutations.
/// Captures OldValues / NewValues diff for security and compliance.
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    PlatformDbContext auditDb) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is EaiosDbContext db)
            await CaptureAuditEventsAsync(db, cancellationToken);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task CaptureAuditEventsAsync(EaiosDbContext db, CancellationToken ct)
    {
        var auditableEntries = db.ChangeTracker.Entries<TenantEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (auditableEntries.Count == 0) return;

        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var correlationId = httpContextAccessor.HttpContext?.Items["X-Correlation-ID"]?.ToString();

        foreach (var entry in auditableEntries)
        {
            var action = entry.State switch
            {
                EntityState.Added    => $"{entry.Entity.GetType().Name.ToLower()}.created",
                EntityState.Modified => $"{entry.Entity.GetType().Name.ToLower()}.updated",
                EntityState.Deleted  => $"{entry.Entity.GetType().Name.ToLower()}.deleted",
                _                    => "unknown"
            };

            var oldValues = entry.State == EntityState.Added
                ? null
                : SerializeProperties(entry.OriginalValues);

            var newValues = entry.State == EntityState.Deleted
                ? null
                : SerializeCurrentValues(entry.Entity);

            var auditEvent = new AuditEvent
            {
                OrganizationId = entry.Entity.OrganizationId,
                ActorId = currentUser.UserId,
                ActorEmail = currentUser.Email,
                ActorIp = ip,
                ActorType = "User",
                Action = action,
                Module = GetModule(entry.Entity.GetType().Namespace),
                Result = AuditEventResult.Success,
                ResourceId = entry.Entity.Id,
                ResourceType = entry.Entity.GetType().Name,
                OldValuesJson = oldValues,
                NewValuesJson = newValues,
                CorrelationId = correlationId
            };

            auditDb.AuditEvents.Add(auditEvent);
        }

        await auditDb.SaveChangesAsync(ct);
    }

    private static string? SerializeProperties(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues values)
    {
        try
        {
            var dict = values.Properties
                .Where(p => !SensitiveProperties.Contains(p.Name))
                .ToDictionary(p => p.Name, p => values[p]);
            return JsonSerializer.Serialize(dict);
        }
        catch { return null; }
    }

    private static string? SerializeCurrentValues(object entity)
    {
        try
        {
            var type = entity.GetType();
            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => !SensitiveProperties.Contains(p.Name))
                .ToDictionary(p => p.Name, p => p.GetValue(entity));
            return JsonSerializer.Serialize(props);
        }
        catch { return null; }
    }

    private static string? GetModule(string? ns) => ns?.Split('.').LastOrDefault()?.ToLowerInvariant();

    private static readonly HashSet<string> SensitiveProperties =
    [
        "PasswordHash", "RefreshTokenHash", "CredentialsEncrypted",
        "SecretEncrypted", "BackupCodesJson", "KeyHash"
    ];
}
