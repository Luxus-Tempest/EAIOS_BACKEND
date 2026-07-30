using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Shared.Primitives;
using EAIOS.Api.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EAIOS.Api.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Intercepte chaque SaveChanges pour enregistrer automatiquement un AuditEvent
/// pour toute mutation d'entité TenantEntity.
/// Ne capture jamais les champs sensibles (mots de passe, tokens, clés).
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    PlatformDbContext auditDb) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly HashSet<string> _sensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "RefreshTokenHash", "CredentialsEncrypted",
        "SecretEncrypted", "BackupCodesJson", "KeyHash", "PasswordResetToken",
        "EmailVerificationToken", "AccessTokenJti"
    };

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        if (eventData.Context is EaiosDbContext db)
            await CaptureAsync(db, ct);

        return await base.SavingChangesAsync(eventData, result, ct);
    }

    private async Task CaptureAsync(EaiosDbContext db, CancellationToken ct)
    {
        var entries = db.ChangeTracker.Entries<TenantEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return;

        var http          = httpContextAccessor.HttpContext;
        var correlationId = http?.Items["X-Correlation-ID"]?.ToString();
        var ip            = http?.Connection.RemoteIpAddress?.ToString();
        var ua            = http?.Request.Headers.UserAgent.ToString();

        var events = new List<AuditEvent>(entries.Count);

        foreach (var entry in entries)
        {
            var typeName = entry.Entity.GetType().Name;
            var action = entry.State switch
            {
                EntityState.Added    => $"{typeName}.created",
                EntityState.Modified => entry.Entity.IsDeleted ? $"{typeName}.soft_deleted" : $"{typeName}.updated",
                EntityState.Deleted  => $"{typeName}.hard_deleted",
                _                    => $"{typeName}.unknown"
            };

            string? oldJson = null, newJson = null;

            if (entry.State == EntityState.Modified)
            {
                var changed = entry.Properties
                    .Where(p => p.IsModified && !_sensitiveFields.Contains(p.Metadata.Name))
                    .ToDictionary(p => p.Metadata.Name, p => new { Old = p.OriginalValue, New = p.CurrentValue });

                if (changed.Count > 0)
                {
                    oldJson = JsonSerializer.Serialize(changed.ToDictionary(k => k.Key, k => k.Value.Old), _jsonOptions);
                    newJson = JsonSerializer.Serialize(changed.ToDictionary(k => k.Key, k => k.Value.New), _jsonOptions);
                }
            }
            else if (entry.State == EntityState.Added)
            {
                var vals = entry.Properties
                    .Where(p => !_sensitiveFields.Contains(p.Metadata.Name))
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                newJson = JsonSerializer.Serialize(vals, _jsonOptions);
            }

            events.Add(new AuditEvent
            {
                OrganizationId   = entry.Entity.OrganizationId,
                ActorId          = currentUser.UserId,
                ActorEmail       = currentUser.Email,
                ActorIp          = ip,
                ActorUserAgent   = ua,
                ActorType        = "User",
                Action           = action,
                Module           = entry.Entity.GetType().Namespace?.Split('.').LastOrDefault()?.ToLowerInvariant(),
                Result           = AuditEventResult.Success,
                ResourceId       = entry.Entity.Id,
                ResourceType     = typeName,
                OldValuesJson    = oldJson,
                NewValuesJson    = newJson,
                CorrelationId    = correlationId
            });
        }

        await auditDb.AuditEvents.AddRangeAsync(events, ct);
        await auditDb.SaveChangesAsync(ct);
    }
}
