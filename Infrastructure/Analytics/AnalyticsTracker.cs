using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Analytics;
using EAIOS.Api.Infrastructure.Persistence;
using System.Text.Json;

namespace EAIOS.Api.Infrastructure.Analytics;

/// <summary>
/// Enregistre les évènements analytiques qui alimentent les tableaux de bord.
/// Sans cette écriture, <c>AnalyticsQueryService</c> n'aurait aucune donnée à agréger.
/// Le tracking ne doit jamais faire échouer l'opération métier appelante : toute
/// exception est journalisée puis avalée.
/// </summary>
public interface IAnalyticsTracker
{
    Task TrackAsync(string eventType, Guid? resourceId = null, string? resourceType = null,
        bool isSuccessful = true, long durationMs = 0, object? properties = null,
        Guid? workspaceId = null, CancellationToken ct = default);
}

/// <summary>Noms canoniques des évènements — évite les fautes de frappe côté appelants.</summary>
public static class AnalyticsEventTypes
{
    public const string DocumentUploaded   = "document.uploaded";
    public const string DocumentDownloaded = "document.downloaded";
    public const string DocumentDeleted    = "document.deleted";
    public const string DocumentViewed     = "document.viewed";

    public const string SearchExecuted     = "search.executed";
    public const string SearchZeroResults  = "search.zero_results";
    public const string SearchAsked        = "search.asked";

    public const string AgentExecuted      = "agent.executed";
    public const string AgentFailed        = "agent.failed";

    public const string WorkflowStarted    = "workflow.started";
    public const string WorkflowCompleted  = "workflow.completed";

    public const string KnowledgeAsked     = "knowledge.asked";
    public const string KnowledgePublished = "knowledge.published";

    public const string UserLoggedIn       = "user.logged_in";
    public const string UserInvited        = "user.invited";
}

public sealed class AnalyticsTracker(
    EaiosDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AnalyticsTracker> logger) : IAnalyticsTracker
{
    public async Task TrackAsync(string eventType, Guid? resourceId = null, string? resourceType = null,
        bool isSuccessful = true, long durationMs = 0, object? properties = null,
        Guid? workspaceId = null, CancellationToken ct = default)
    {
        try
        {
            if (!tenantContext.IsResolved)
                return;

            var propertiesJson = properties is null ? "{}" : JsonSerializer.Serialize(properties);
            var http = httpContextAccessor.HttpContext;

            var evt = AnalyticsEvent.Create(
                organizationId: tenantContext.OrganizationId,
                eventType:      eventType,
                actorType:      currentUser.UserId.HasValue ? "User" : "System",
                actorId:        currentUser.UserId,
                resourceId:     resourceId,
                resourceType:   resourceType,
                isSuccessful:   isSuccessful,
                durationMs:     durationMs,
                propertiesJson: propertiesJson,
                workspaceId:    workspaceId,
                sessionId:      http?.TraceIdentifier);

            await db.AnalyticsEvents.AddAsync(evt, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Échec d'enregistrement de l'évènement analytique {EventType}", eventType);
        }
    }
}
