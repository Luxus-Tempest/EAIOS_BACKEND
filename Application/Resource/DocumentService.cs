using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Analytics;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Storage;

namespace EAIOS.Api.Application.Resource;

public interface IDocumentService
{
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
    Task<Document> RestoreDocumentAsync(Guid id, CancellationToken ct = default);
    Task PurgeDocumentAsync(Guid id, CancellationToken ct = default);
    Task<Document> MoveDocumentAsync(Guid id, Guid? folderId, CancellationToken ct = default);

    /// <summary>Ouvre le contenu binaire de la version courante (ou d'une version précise).</summary>
    Task<DocumentDownload> DownloadAsync(Guid id, Guid? versionId = null, CancellationToken ct = default);

    /// <summary>Repromeut une ancienne version comme version courante du document.</summary>
    Task<DocumentVersion> RestoreVersionAsync(Guid documentId, Guid versionId, Guid actorId, CancellationToken ct = default);

    Task<IReadOnlyList<MetadataValue>> GetMetadataAsync(Guid documentId, CancellationToken ct = default);
    Task<IReadOnlyList<MetadataValue>> SetMetadataAsync(Guid tenantId, Guid documentId,
        IReadOnlyList<MetadataValueInput> values, Guid actorId, CancellationToken ct = default);

    Task<DocumentShare> CreatePublicLinkAsync(Guid tenantId, Guid documentId, SharePermission permission,
        Guid actorId, DateTime? expiresAt, CancellationToken ct = default);
    Task<DocumentDownload> DownloadByPublicLinkAsync(string token, CancellationToken ct = default);

    Task<LegalHold> CreateLegalHoldAsync(Guid tenantId, Guid documentId, string reason, Guid actorId,
        string? caseReference, CancellationToken ct = default);
    Task ReleaseLegalHoldAsync(Guid documentId, Guid holdId, Guid actorId, string reason, CancellationToken ct = default);
}

public sealed record DocumentDownload(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum);

public sealed class DocumentService(
    IDocumentRepository documentRepo,
    IDocumentVersionRepository versionRepo,
    IDocumentShareRepository shareRepo,
    IMetadataValueRepository metadataRepo,
    ILegalHoldRepository holdRepo,
    IStorageService storage,
    IAnalyticsTracker analytics,
    ILogger<DocumentService> logger) : IDocumentService
{
    // ═════════════════════════════════════════════════════════════════════════
    // CYCLE DE VIE
    // ═════════════════════════════════════════════════════════════════════════

    public async Task DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        var holds = await holdRepo.GetActiveByDocumentAsync(id, ct);
        if (holds.Count > 0)
            throw new InvalidOperationException("LEGAL_HOLD_ACTIVE");

        doc.MoveToTrash();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);

        await analytics.TrackAsync(AnalyticsEventTypes.DocumentDeleted,
            resourceId: id, resourceType: "Document", ct: ct);
    }

    public async Task<Document> RestoreDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        doc.Restore();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);
        return doc;
    }

    /// <summary>
    /// Suppression définitive : retire aussi les fichiers du stockage objet.
    /// Un document sous hold légal ne peut jamais être purgé.
    /// </summary>
    public async Task PurgeDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        var holds = await holdRepo.GetActiveByDocumentAsync(id, ct);
        if (holds.Count > 0)
            throw new InvalidOperationException("LEGAL_HOLD_ACTIVE");

        if (doc.Status != ResourceStatus.Trashed)
            throw new InvalidOperationException("Seul un document déjà mis à la corbeille peut être purgé.");

        var versions = await versionRepo.GetByDocumentAsync(id, ct);
        foreach (var version in versions)
        {
            try
            {
                await storage.DeleteAsync(version.StorageKey, ct);
            }
            catch (Exception ex)
            {
                // Un objet déjà absent du stockage ne doit pas empêcher la purge en base.
                logger.LogWarning(ex, "Fichier {Key} non supprimé lors de la purge du document {DocumentId}.",
                    version.StorageKey, id);
            }
        }

        documentRepo.SoftDelete(doc);
        await documentRepo.SaveAsync(ct);

        logger.LogInformation("Document {DocumentId} purgé ({Count} fichier(s) supprimé(s)).", id, versions.Count);
    }

    public async Task<Document> MoveDocumentAsync(Guid id, Guid? folderId, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        doc.MoveToFolder(folderId);
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);
        return doc;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TÉLÉCHARGEMENT & VERSIONS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<DocumentDownload> DownloadAsync(Guid id, Guid? versionId = null, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        var version = versionId.HasValue
            ? await versionRepo.GetByIdAsync(versionId.Value, ct)
            : await versionRepo.GetCurrentAsync(id, ct);

        if (version is null || version.DocumentId != id)
            throw new KeyNotFoundException("Version de document introuvable.");

        if (version.Status == DocumentVersionStatus.Quarantined)
            throw new InvalidOperationException("Ce fichier est en quarantaine : l'analyse antivirus a échoué.");

        var stream = await storage.OpenReadAsync(version.StorageKey, ct)
            ?? throw new KeyNotFoundException("Le fichier associé est introuvable sur le stockage.");

        doc.IncrementDownload();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);

        await analytics.TrackAsync(AnalyticsEventTypes.DocumentDownloaded,
            resourceId: id, resourceType: "Document",
            properties: new { versionNumber = version.VersionNumber }, ct: ct);

        return new DocumentDownload(
            stream,
            version.OriginalFileName,
            string.IsNullOrWhiteSpace(version.MimeType) ? "application/octet-stream" : version.MimeType,
            version.FileSizeBytes,
            version.Checksum);
    }

    /// <summary>
    /// Restaure une version antérieure en créant une NOUVELLE version qui pointe
    /// sur le même objet stocké. L'historique reste ainsi strictement append-only.
    /// </summary>
    public async Task<DocumentVersion> RestoreVersionAsync(
        Guid documentId, Guid versionId, Guid actorId, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(documentId, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        var source = await versionRepo.GetByIdAsync(versionId, ct);
        if (source is null || source.DocumentId != documentId)
            throw new KeyNotFoundException("Version de document introuvable.");

        var versions = await versionRepo.GetByDocumentAsync(documentId, ct);

        if (source.IsCurrent)
            throw new InvalidOperationException("Cette version est déjà la version courante.");

        foreach (var current in versions.Where(v => v.IsCurrent))
        {
            current.SetAsCurrent(false);
            versionRepo.Update(current);
        }

        var nextNumber = versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1;

        var restored = DocumentVersion.Create(
            doc.OrganizationId, documentId, nextNumber,
            source.StorageKey, source.OriginalFileName, source.MimeType, source.FileSizeBytes,
            actorId, changeNote: $"Restauration de la version {source.VersionNumber}.");

        await versionRepo.AddAsync(restored, ct);

        doc.SetCurrentVersion(restored.Id, restored.MimeType, restored.FileSizeBytes,
            Path.GetExtension(restored.OriginalFileName), source.PageCount);
        documentRepo.Update(doc);

        await versionRepo.SaveAsync(ct);
        await documentRepo.SaveAsync(ct);

        logger.LogInformation("Version {SourceVersion} du document {DocumentId} restaurée en version {NewVersion}.",
            source.VersionNumber, documentId, nextNumber);

        return restored;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MÉTADONNÉES
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<MetadataValue>> GetMetadataAsync(Guid documentId, CancellationToken ct = default)
    {
        _ = await documentRepo.GetByIdAsync(documentId, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        return await metadataRepo.GetByResourceAsync(documentId, ct);
    }

    /// <summary>
    /// Applique un lot de valeurs de métadonnées. Une valeur existante est mise à jour,
    /// une valeur absente est créée, et une valeur explicitement nulle est effacée.
    /// </summary>
    public async Task<IReadOnlyList<MetadataValue>> SetMetadataAsync(
        Guid tenantId, Guid documentId, IReadOnlyList<MetadataValueInput> values,
        Guid actorId, CancellationToken ct = default)
    {
        _ = await documentRepo.GetByIdAsync(documentId, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        foreach (var input in values)
        {
            if (string.IsNullOrWhiteSpace(input.FieldKey))
                throw new ArgumentException("Chaque métadonnée doit porter une clé de champ.");

            var key      = input.FieldKey.Trim();
            var existing = await metadataRepo.FindAsync(documentId, key, ct);

            if (existing is null)
            {
                if (input.Value is null) continue;   // rien à créer pour une valeur nulle

                var created = MetadataValue.Create(tenantId, documentId, key, input.Value, actorId);
                await metadataRepo.AddAsync(created, ct);
            }
            else if (input.Value is null)
            {
                metadataRepo.SoftDelete(existing);
            }
            else
            {
                existing.SetValue(input.Value);
                metadataRepo.Update(existing);
            }
        }

        await metadataRepo.SaveAsync(ct);
        return await metadataRepo.GetByResourceAsync(documentId, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PARTAGE PAR LIEN PUBLIC
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<DocumentShare> CreatePublicLinkAsync(
        Guid tenantId, Guid documentId, SharePermission permission,
        Guid actorId, DateTime? expiresAt, CancellationToken ct = default)
    {
        _ = await documentRepo.GetByIdAsync(documentId, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        if (expiresAt.HasValue && expiresAt <= DateTime.UtcNow)
            throw new ArgumentException("La date d'expiration doit être dans le futur.");

        var share = DocumentShare.CreatePublicLink(tenantId, documentId, permission, actorId, expiresAt);

        await shareRepo.AddAsync(share, ct);
        await shareRepo.SaveAsync(ct);

        return share;
    }

    /// <summary>Résout un lien public et renvoie le contenu, en comptabilisant l'accès.</summary>
    public async Task<DocumentDownload> DownloadByPublicLinkAsync(string token, CancellationToken ct = default)
    {
        var share = await shareRepo.FindByTokenAsync(token, ct)
            ?? throw new KeyNotFoundException("Lien de partage invalide.");

        if (share.IsExpired)
            throw new InvalidOperationException("Ce lien de partage a expiré.");

        var version = await versionRepo.GetCurrentAsync(share.DocumentId, ct)
            ?? throw new KeyNotFoundException("Aucune version disponible pour ce document.");

        var stream = await storage.OpenReadAsync(version.StorageKey, ct)
            ?? throw new KeyNotFoundException("Le fichier associé est introuvable sur le stockage.");

        share.RecordAccess();
        shareRepo.Update(share);
        await shareRepo.SaveAsync(ct);

        return new DocumentDownload(
            stream,
            version.OriginalFileName,
            string.IsNullOrWhiteSpace(version.MimeType) ? "application/octet-stream" : version.MimeType,
            version.FileSizeBytes,
            version.Checksum);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HOLDS LÉGAUX
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<LegalHold> CreateLegalHoldAsync(
        Guid tenantId, Guid documentId, string reason, Guid actorId,
        string? caseReference, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(documentId, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        var hold = LegalHold.Create(tenantId, documentId, reason, actorId, caseReference);
        await holdRepo.AddAsync(hold, ct);

        // Le drapeau porté par le document évite une requête sur les holds
        // à chaque tentative de suppression.
        doc.SetLegalHold(true);
        documentRepo.Update(doc);

        await holdRepo.SaveAsync(ct);
        await documentRepo.SaveAsync(ct);

        logger.LogInformation("Hold légal posé sur le document {DocumentId} — motif : {Reason}", documentId, reason);
        return hold;
    }

    public async Task ReleaseLegalHoldAsync(
        Guid documentId, Guid holdId, Guid actorId, string reason, CancellationToken ct = default)
    {
        var hold = await holdRepo.GetByIdAsync(holdId, ct)
            ?? throw new KeyNotFoundException("Hold introuvable.");

        if (hold.DocumentId != documentId)
            throw new InvalidOperationException("Ce hold ne concerne pas le document indiqué.");

        hold.Release(actorId, reason);
        holdRepo.Update(hold);
        await holdRepo.SaveAsync(ct);

        // Le document ne redevient libérable que si plus aucun hold actif ne subsiste.
        var remaining = await holdRepo.GetActiveByDocumentAsync(documentId, ct);
        if (remaining.Count == 0)
        {
            var doc = await documentRepo.GetByIdAsync(documentId, ct);
            if (doc is not null)
            {
                doc.SetLegalHold(false);
                documentRepo.Update(doc);
                await documentRepo.SaveAsync(ct);
            }
        }

        logger.LogInformation("Hold légal {HoldId} levé sur le document {DocumentId}.", holdId, documentId);
    }
}
