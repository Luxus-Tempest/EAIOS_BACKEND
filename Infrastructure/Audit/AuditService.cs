using EAIOS.Api.Domain.Platform;
using EAIOS.Api.Infrastructure.Persistence;

namespace EAIOS.Api.Infrastructure.Audit;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IAuditService
{
    Task LogAsync(AuditEvent evt, CancellationToken ct = default);
    Task LogAsync(
        Guid   organizationId,
        string action,
        string actorType,
        AuditEventResult result,
        Guid?  actorId         = null,
        string? actorEmail     = null,
        string? actorIp        = null,
        Guid?  resourceId      = null,
        string? resourceType   = null,
        string? resourceName   = null,
        string? module         = null,
        string? correlationId  = null,
        string? failureReason  = null,
        CancellationToken ct  = default);
}

// ── Implementation ────────────────────────────────────────────────────────────

public sealed class AuditService(
    PlatformDbContext platformDb,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(AuditEvent evt, CancellationToken ct = default)
    {
        try
        {
            await platformDb.AuditEvents.AddAsync(evt, ct);
            await platformDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // L'audit ne doit JAMAIS faire échouer une opération métier
            logger.LogError(ex, "Failed to write audit event {Action} for org {OrgId}", evt.Action, evt.OrganizationId);
        }
    }

    public Task LogAsync(
        Guid   organizationId,
        string action,
        string actorType,
        AuditEventResult result,
        Guid?  actorId         = null,
        string? actorEmail     = null,
        string? actorIp        = null,
        Guid?  resourceId      = null,
        string? resourceType   = null,
        string? resourceName   = null,
        string? module         = null,
        string? correlationId  = null,
        string? failureReason  = null,
        CancellationToken ct  = default)
    {
        var evt = new AuditEvent
        {
            OrganizationId = organizationId,
            Action         = action,
            ActorType      = actorType,
            ActorId        = actorId,
            ActorEmail     = actorEmail,
            ActorIp        = actorIp,
            Result         = result,
            FailureReason  = failureReason,
            ResourceId     = resourceId,
            ResourceType   = resourceType,
            ResourceName   = resourceName,
            Module         = module,
            CorrelationId  = correlationId
        };
        return LogAsync(evt, ct);
    }
}
