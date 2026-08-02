using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using EAIOS.Api.Infrastructure.Storage;

namespace EAIOS.Api.Application.Resource;

public interface IDocumentService
{
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
    Task<Document> RestoreDocumentAsync(Guid id, CancellationToken ct = default);
    Task<LegalHold> CreateLegalHoldAsync(Guid tenantId, Guid documentId, string reason, Guid actorId, string? caseReference, CancellationToken ct = default);
    Task ReleaseLegalHoldAsync(Guid documentId, Guid holdId, Guid actorId, string reason, CancellationToken ct = default);
}

public sealed class DocumentService(
    IDocumentRepository documentRepo,
    ILegalHoldRepository holdRepo) : IDocumentService
{
    public async Task DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Document introuvable.");

        var holds = await holdRepo.GetActiveByDocumentAsync(id, ct);
        if (holds.Count > 0)
            throw new InvalidOperationException("LEGAL_HOLD_ACTIVE");

        doc.MoveToTrash();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);
    }

    public async Task<Document> RestoreDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await documentRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Document introuvable.");
        doc.Restore();
        documentRepo.Update(doc);
        await documentRepo.SaveAsync(ct);
        return doc;
    }

    public async Task<LegalHold> CreateLegalHoldAsync(Guid tenantId, Guid documentId, string reason, Guid actorId, string? caseReference, CancellationToken ct = default)
    {
        var hold = LegalHold.Create(tenantId, documentId, reason, actorId, caseReference);
        await holdRepo.AddAsync(hold, ct);
        await holdRepo.SaveAsync(ct);
        return hold;
    }

    public async Task ReleaseLegalHoldAsync(Guid documentId, Guid holdId, Guid actorId, string reason, CancellationToken ct = default)
    {
        var hold = await holdRepo.GetByIdAsync(holdId, ct) ?? throw new KeyNotFoundException("Hold introuvable.");
        if (hold.DocumentId != documentId) throw new InvalidOperationException("Discordance DocumentId");

        hold.Release(actorId, reason);
        holdRepo.Update(hold);
        await holdRepo.SaveAsync(ct);
    }
}
