using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Extraction;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Application.Knowledge;

/// <summary>
/// Fait parler un document déposé.
///
/// <para>
/// À chaque nouvelle version : le texte est extrait page par page, découpé en
/// respectant la structure, et rangé dans une fiche de connaissance
/// <b>dérivée</b> du document (source <c>AutoExtracted</c>). La fiche hérite de
/// l'emplacement du document ; sa classification reste celle du document, que
/// l'indexation lit par jointure — un extrait de contrat confidentiel n'est
/// jamais consultable au-delà de son document.
/// </para>
/// <para>
/// La fiche est publiée d'office : le document <i>est</i> l'autorité, et il
/// est déjà dans la base. Elle n'est pas « vérifiée par un humain » pour autant,
/// et le dit.
/// </para>
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>Extrait, découpe et indexe une version. Le tenant doit être résolu.</summary>
    Task<IngestionOutcome> IngestVersionAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>Redemande l'extraction de la version courante d'un document.</summary>
    Task<IngestionOutcome> ReingestDocumentAsync(Guid documentId, CancellationToken ct = default);
}

public sealed record IngestionOutcome(
    Guid DocumentId,
    Guid VersionId,
    bool Extracted,
    int PageCount,
    int ChunkCount,
    string? Language,
    string? Reason);

public sealed class DocumentIngestionService(
    EaiosDbContext db,
    IStorageService storage,
    ITextExtractor extractor,
    IAgentRuntimeClient runtime,
    ILogger<DocumentIngestionService> logger) : IDocumentIngestionService
{
    /// <summary>Au-delà, le texte complet n'est pas recopié sur le document (les segments, eux, sont complets).</summary>
    private const int MaxStoredTextLength = 500_000;

    public async Task<IngestionOutcome> ReingestDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var version = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.IsCurrent)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Le document n'a pas de version courante.");

        return await IngestVersionAsync(version.Id, ct);
    }

    public async Task<IngestionOutcome> IngestVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new KeyNotFoundException("Version introuvable.");
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == version.DocumentId, ct)
            ?? throw new KeyNotFoundException("Document introuvable.");

        // Aucun antivirus n'est branché : l'étape est franchie en le disant,
        // plutôt que d'inventer un résultat. `MarkScanned` est la porte du
        // cycle de vie — sans elle, la version resterait « déposée » à vie.
        if (version.Status is DocumentVersionStatus.Uploaded or DocumentVersionStatus.PendingScan)
            version.MarkScanned(passed: true, result: "no-scanner");

        if (!extractor.Supports(version.MimeType, version.OriginalFileName))
        {
            // Format illisible sans reconnaissance de caractères (image, PDF
            // numérisé sans couche texte…). Le document reste trouvable par ses
            // métadonnées ; son statut dit pourquoi il ne l'est pas par son contenu.
            version.MarkParsed(null, null, null, ocrApplied: false);
            version.MarkIndexed(null);
            document.SetIndexingFailed();
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Document {DocumentId} : format {Mime} non extractible.", document.Id, version.MimeType);
            return new IngestionOutcome(document.Id, version.Id, false, 0, 0, null,
                "Format non pris en charge par l'extraction de texte (reconnaissance de caractères non embarquée).");
        }

        await using var stream = await storage.OpenReadAsync(version.StorageKey, ct);
        if (stream is null)
        {
            document.SetIndexingFailed();
            await db.SaveChangesAsync(ct);
            return new IngestionOutcome(document.Id, version.Id, false, 0, 0, null, "Fichier introuvable sur le stockage.");
        }

        ExtractedDocument? extracted;
        try
        {
            extracted = await extractor.ExtractAsync(stream, version.MimeType, version.OriginalFileName, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Extraction impossible pour la version {VersionId}.", version.Id);
            document.SetIndexingFailed();
            version.MarkParsed(null, null, null, false);
            await db.SaveChangesAsync(ct);
            return new IngestionOutcome(document.Id, version.Id, false, 0, 0, null, $"Extraction en échec : {ex.Message}");
        }

        if (extracted is null || extracted.IsEmpty)
        {
            version.MarkParsed(null, extracted?.PageCount, null, false);
            version.MarkIndexed(null);
            document.SetIndexingFailed();
            await db.SaveChangesAsync(ct);
            return new IngestionOutcome(document.Id, version.Id, false, extracted?.PageCount ?? 0, 0, null,
                "Aucun texte lisible : le fichier est probablement une image ou un PDF numérisé.");
        }

        var fullText = extracted.FullText;
        var stored = fullText.Length > MaxStoredTextLength ? fullText[..MaxStoredTextLength] : fullText;

        version.MarkParsed(stored, extracted.PageCount, extracted.Language, extracted.OcrApplied);

        // ── La fiche dérivée ─────────────────────────────────────────────────
        var item = await db.KnowledgeItems
            .FirstOrDefaultAsync(i => i.SourceDocumentId == document.Id && i.Source == KnowledgeItemSource.AutoExtracted, ct);

        if (item is null)
        {
            item = KnowledgeItem.Create(
                document.OrganizationId, document.Title, KnowledgeItemType.Reference,
                KnowledgeItemSource.AutoExtracted, document.OwnerId, content: stored, sourceDocumentId: document.Id);
            item.Publish(document.OwnerId);
            await db.KnowledgeItems.AddAsync(item, ct);
        }
        else
        {
            item.Update(document.Title, stored, null, null, null);
            // Les anciens segments partent avec l'ancienne version : un texte
            // modifié ne doit plus être cité depuis sa version précédente.
            var stale = await db.KnowledgeChunks.Where(c => c.ItemId == item.Id).ToListAsync(ct);
            db.KnowledgeChunks.RemoveRange(stale);
        }

        item.SetSourceVersion(version.Id);
        item.SetLocation(document.WorkspaceId, document.DepartmentId);
        item.Update(null, null, Summary(fullText), document.Tags, extracted.Language);

        var chunks = StructuredChunker.Materialize(document.OrganizationId, item.Id, StructuredChunker.Split(extracted.Pages));
        await db.KnowledgeChunks.AddRangeAsync(chunks, ct);

        version.MarkIndexed(null);
        document.SetIndexed(stored, extracted.PageCount, extracted.Language);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Document {DocumentId} extrait : {Pages} page(s), {Chunks} segment(s), langue {Language}.",
            document.Id, extracted.PageCount, chunks.Count, extracted.Language ?? "?");

        // Les vecteurs suivent : le runtime réindexe l'organisation. Un runtime
        // absent n'empêche pas l'extraction — il rattrapera au prochain appel.
        try
        {
            await runtime.ReindexAsync(document.OrganizationId, ct);
        }
        catch (AgentRuntimeUnavailableException ex)
        {
            logger.LogWarning(ex, "Runtime injoignable : l'organisation {OrganizationId} sera réindexée plus tard.", document.OrganizationId);
        }

        return new IngestionOutcome(document.Id, version.Id, true, extracted.PageCount, chunks.Count, extracted.Language, null);
    }

    /// <summary>Le début du texte, sur une ligne : ce que la liste montre sous le titre.</summary>
    private static string Summary(string text)
    {
        var line = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return line.Length <= 280 ? line : line[..279].TrimEnd() + "…";
    }
}
