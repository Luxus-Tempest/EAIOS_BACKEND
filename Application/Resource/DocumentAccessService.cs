using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Audit;
using EAIOS.Api.Infrastructure.Security;

namespace EAIOS.Api.Application.Resource;

/// <summary>Ce qu'une personne a le droit de voir, sous forme applicable à une requête de liste.</summary>
public sealed record DocumentVisibility(
    ResourceClassification MaxClassification,
    IReadOnlySet<Guid> ExplicitlyAllowed,
    IReadOnlySet<Guid> ExplicitlyDenied,
    bool Unrestricted);

/// <summary>
/// La lecture des documents, gouvernée.
///
/// <para>
/// Jusqu'ici, la liste et l'ouverture d'un document ne vérifiaient rien : tout
/// membre authentifié voyait tout, jusqu'au niveau le plus élevé. Ce service
/// applique le <b>plafond de classification</b> de la personne
/// (<see cref="ClassificationCeiling"/>), les <b>ACL</b> nominatives et les
/// <b>politiques</b> — la même chaîne que le laissez-passer de l'agent — et
/// journalise chaque consultation.
/// </para>
/// </summary>
public interface IDocumentAccessService
{
    Task<DocumentVisibility> GetVisibilityAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanReadAsync(Document document, Guid userId, CancellationToken ct = default);

    /// <summary>Journalise une consultation ou un téléchargement, sous l'identité de la personne.</summary>
    Task RecordReadAsync(Document document, string action, CancellationToken ct = default);
}

public sealed class DocumentAccessService(
    IPermissionService permissions,
    ICurrentUser currentUser,
    IAuditService audit,
    IHttpContextAccessor httpContextAccessor) : IDocumentAccessService
{
    public async Task<DocumentVisibility> GetVisibilityAsync(Guid userId, CancellationToken ct = default)
    {
        if (currentUser.IsPlatformAdmin)
            return new DocumentVisibility(ResourceClassification.StrictlyConfidential, new HashSet<Guid>(), new HashSet<Guid>(), Unrestricted: true);

        var ceiling = await permissions.GetClassificationCeilingAsync(userId, ct);
        var (allowed, denied) = await permissions.GetExplicitGrantsAsync(userId, Permissions.ResourceRead, "Document", ct);
        return new DocumentVisibility(ceiling, allowed, denied, Unrestricted: false);
    }

    public async Task<bool> CanReadAsync(Document document, Guid userId, CancellationToken ct = default)
    {
        if (currentUser.IsPlatformAdmin) return true;

        var decision = await permissions.GetAclDecisionAsync(userId, Permissions.ResourceRead, document.Id, "Document", ct);
        if (decision == AclDecision.Deny) return false;

        // Au-dessus du plafond, seule une autorisation nominative ouvre le document.
        var ceiling = await permissions.GetClassificationCeilingAsync(userId, ct);
        if (document.Classification > ceiling)
            return decision == AclDecision.Allow;

        return await permissions.HasResourcePermissionAsync(
            userId, Permissions.ResourceRead, document.Id, "Document", ResourceAttributes.Of(document), ct);
    }

    public async Task RecordReadAsync(Document document, string action, CancellationToken ct = default)
    {
        var http = httpContextAccessor.HttpContext;
        await audit.LogAsync(
            organizationId: document.OrganizationId,
            action:         action,
            actorType:      "User",
            result:         Domain.Platform.AuditEventResult.Success,
            actorId:        currentUser.UserId,
            actorEmail:     currentUser.Email,
            actorIp:        http?.Connection.RemoteIpAddress?.ToString(),
            resourceId:     document.Id,
            resourceType:   "Document",
            resourceName:   document.Title,
            module:         "Resource",
            correlationId:  http?.Items["X-Correlation-ID"]?.ToString(),
            ct:             ct);
    }
}
