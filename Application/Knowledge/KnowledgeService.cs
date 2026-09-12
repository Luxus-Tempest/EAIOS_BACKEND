using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Knowledge;

namespace EAIOS.Api.Application.Knowledge;

public sealed class KnowledgeService(
    IKnowledgeItemRepository itemRepo,
    IKnowledgeChunkRepository chunkRepo,
    IKnowledgePackRepository packRepo,
    EAIOS.Api.Infrastructure.Analytics.IAnalyticsTracker analytics,
    EAIOS.Api.Infrastructure.Security.IAgentContextService contextService,
    IAgentRuntimeClient runtime,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    ILogger<KnowledgeService> logger) : IKnowledgeService
{
    // ═════════════════════════════════════════════════════════════════════════
    // FICHES
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<KnowledgeItem> CreateItemAsync(Guid tenantId, CreateKnowledgeItemRequest request, Guid actorId, CancellationToken ct = default)
    {
        var item = KnowledgeItem.Create(tenantId, request.Title, request.Type, KnowledgeItemSource.Manual, actorId,
            request.Content, request.SourceDocumentId);

        // Le contrat portait déjà le pack, le résumé, les étiquettes, la langue
        // et l'espace ; le contrôleur les laissait tomber.
        item.Update(null, null, request.Summary, request.Tags, request.Language);
        item.SetPack(request.PackId);
        item.SetLocation(request.WorkspaceId, null);

        await itemRepo.AddAsync(item, ct);
        await itemRepo.SaveAsync(ct);

        await ReplaceChunksAsync(item, ct);
        await AdjustPackCountAsync(null, request.PackId, ct);
        return item;
    }

    public async Task<KnowledgeItem> UpdateItemAsync(Guid id, UpdateKnowledgeItemRequest request, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");
        var previousPack = item.PackId;

        item.Update(request.Title, request.Content, request.Summary, request.Tags, request.Language);
        if (request.PackId is not null || request.ClearPack)
            item.SetPack(request.ClearPack ? null : request.PackId);

        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);

        // Un contenu modifié doit être redécoupé : sinon l'agent continue de
        // citer l'ancien texte depuis des segments périmés.
        if (request.Content is not null)
            await ReplaceChunksAsync(item, ct);

        await AdjustPackCountAsync(previousPack, item.PackId, ct);

        if (item.Status == KnowledgeItemStatus.Published)
            await RequestReindexAsync(item.OrganizationId, ct);

        return item;
    }

    public async Task<KnowledgeItem> PublishItemAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");

        item.Publish(actorId);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);

        // Publier, c'est rendre citable : les vecteurs doivent suivre.
        await RequestReindexAsync(item.OrganizationId, ct);
        return item;
    }

    public async Task<KnowledgeItem> ValidateItemAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");

        item.Validate(true, actorId);
        // Une fiche relue par une personne est la plus sûre de toutes.
        item.SetConfidence(1.0f);
        itemRepo.Update(item);
        await itemRepo.SaveAsync(ct);

        return item;
    }

    public async Task DeleteItemAsync(Guid id, CancellationToken ct = default)
    {
        var item = await itemRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Item introuvable.");

        // Les segments partent avec la fiche ; la réindexation retire les vecteurs.
        foreach (var chunk in await chunkRepo.GetByItemAsync(id, ct))
            chunkRepo.SoftDelete(chunk);

        itemRepo.SoftDelete(item);
        await chunkRepo.SaveAsync(ct);
        await itemRepo.SaveAsync(ct);

        await AdjustPackCountAsync(item.PackId, null, ct);
        await RequestReindexAsync(item.OrganizationId, ct);
    }

    /// <summary>Remplace les segments d'une fiche par un découpage structuré de son contenu.</summary>
    private async Task ReplaceChunksAsync(KnowledgeItem item, CancellationToken ct)
    {
        foreach (var chunk in await chunkRepo.GetByItemAsync(item.Id, ct))
            chunkRepo.SoftDelete(chunk);

        var chunks = StructuredChunker.Materialize(item.OrganizationId, item.Id, StructuredChunker.Split(item.Content));
        if (chunks.Count > 0)
            await chunkRepo.AddRangeAsync(chunks, ct);

        await chunkRepo.SaveAsync(ct);
    }

    /// <summary>Le compteur d'un pack suit ses affectations, dans les deux sens.</summary>
    private async Task AdjustPackCountAsync(Guid? previous, Guid? next, CancellationToken ct)
    {
        if (previous == next) return;

        if (previous is { } from && await packRepo.GetByIdAsync(from, ct) is { } oldPack)
        {
            oldPack.DecrementItemCount();
            packRepo.Update(oldPack);
        }
        if (next is { } to && await packRepo.GetByIdAsync(to, ct) is { } newPack)
        {
            newPack.IncrementItemCount();
            packRepo.Update(newPack);
        }
        await packRepo.SaveAsync(ct);
    }

    /// <summary>
    /// Demande la resynchronisation des vecteurs. Un runtime absent n'est pas
    /// une erreur métier : la fiche est enregistrée, l'index rattrapera.
    /// </summary>
    private async Task RequestReindexAsync(Guid organizationId, CancellationToken ct)
    {
        try
        {
            await runtime.ReindexAsync(organizationId, ct);
        }
        catch (AgentRuntimeUnavailableException ex)
        {
            logger.LogWarning(ex, "Runtime injoignable : réindexation différée pour {OrganizationId}.", organizationId);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PACKS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<KnowledgePack> CreatePackAsync(Guid tenantId, CreatePackRequest request, Guid actorId, CancellationToken ct = default)
    {
        var pack = KnowledgePack.Create(tenantId, request.Name, actorId, request.Description, request.IsPublic, request.Language, request.Tags);

        await packRepo.AddAsync(pack, ct);
        await packRepo.SaveAsync(ct);

        return pack;
    }

    public async Task<KnowledgePack> UpdatePackAsync(Guid id, UpdatePackRequest request, CancellationToken ct = default)
    {
        var pack = await packRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Pack introuvable.");

        pack.Update(request.Name, request.Description, request.Tags, request.IsPublic, request.Language);
        packRepo.Update(pack);
        await packRepo.SaveAsync(ct);

        return pack;
    }

    /// <summary>
    /// Publier un pack, c'est ouvrir ses fiches aux agents abonnés : l'index du
    /// runtime ne retient que les fiches d'un pack publié, il doit donc suivre.
    /// </summary>
    public async Task<KnowledgePack> PublishPackAsync(Guid id, CancellationToken ct = default)
    {
        var pack = await packRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Pack introuvable.");

        pack.Publish();
        packRepo.Update(pack);
        await packRepo.SaveAsync(ct);

        await RequestReindexAsync(pack.OrganizationId, ct);
        return pack;
    }

    public async Task<KnowledgePack> ArchivePackAsync(Guid id, CancellationToken ct = default)
    {
        var pack = await packRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Pack introuvable.");

        pack.Archive();
        packRepo.Update(pack);
        await packRepo.SaveAsync(ct);

        // Archivé : ses fiches sortent du périmètre citable.
        await RequestReindexAsync(pack.OrganizationId, ct);
        return pack;
    }

    /// <summary>
    /// Supprime un pack. Les fiches ne partent pas avec lui : elles cessent
    /// seulement d'y appartenir, et cessent d'être citables sous ce périmètre.
    /// </summary>
    public async Task DeletePackAsync(Guid id, CancellationToken ct = default)
    {
        var pack = await packRepo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Pack introuvable.");

        foreach (var item in await itemRepo.GetByPackAsync(id, ct))
        {
            item.SetPack(null);
            itemRepo.Update(item);
        }
        await itemRepo.SaveAsync(ct);

        packRepo.SoftDelete(pack);
        await packRepo.SaveAsync(ct);

        await RequestReindexAsync(pack.OrganizationId, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // QUESTION / RÉPONSE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Question/réponse ancrée dans la base documentaire.
    ///
    /// <para>
    /// Le raisonnement est délégué au runtime d'agents : c'est ce qui donne
    /// enfin à cet endpoint les citations à la page et à l'article que le design
    /// exige, et ce qui met fin aux <b>deux RAG parallèles</b> — celui-ci et
    /// <c>SearchService.AskAsync</c> — qui répondaient chacun à leur façon.
    /// </para>
    /// <para>
    /// Le repli lexical reste, mais il ne se déclenche plus en silence : quand
    /// le runtime est injoignable, la réponse le <b>dit</b> par son
    /// <c>RetrievalMode</c>. Une dégradation qui ne s'annonce pas est pire
    /// qu'une panne : elle laisse croire que tout va bien.
    /// </para>
    /// </summary>
    public async Task<AskResponse> AskAsync(string question, Guid? packId, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var tenantId = tenantContext.OrganizationId;
        var actorId  = currentUser.UserId ?? Guid.Empty;

        AskResponse response;
        string mode;

        try
        {
            var context = contextService.IssueForAssistant(
                tenantId, actorId,
                packId.HasValue ? [packId.Value] : [],
                Guid.CreateVersion7());

            var result = await runtime.AskAsync(context.Token, CallerToken(), question, ct);

            if (result.IsFailed)
                throw new AgentRuntimeUnavailableException(result.ErrorMessage ?? "Échec du runtime.");

            response = MapAnswer(result);
            mode     = "agentic";
        }
        catch (Exception ex) when (ex is AgentRuntimeUnavailableException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Runtime injoignable : repli lexical, sans citations.");
            response = await AskLexicallyAsync(question, packId, ct);
            mode     = "lexical-degraded";
        }

        stopwatch.Stop();

        await analytics.TrackAsync(
            EAIOS.Api.Infrastructure.Analytics.AnalyticsEventTypes.KnowledgeAsked,
            resourceType: "KnowledgeItem",
            durationMs:   stopwatch.ElapsedMilliseconds,
            properties: new
            {
                query       = question,
                resultCount = response.Sources.Count,
                mode,
                tokens      = response.PromptTokens + response.CompletionTokens
            },
            ct: ct);

        return response with { RetrievalMode = mode };
    }

    private static AskResponse MapAnswer(RuntimeRunResult result)
    {
        var answer = result.Output;

        return new AskResponse(
            Answer:           answer?.Answer ?? "",
            // Les items cités, dédupliqués : une même fiche peut fonder
            // plusieurs renvois.
            Sources:          answer is null ? [] : answer.Citations
                                  .Where(c => c.KnowledgeItemId.HasValue)
                                  .GroupBy(c => c.KnowledgeItemId!.Value)
                                  .Select(g => new SourceRef(g.Key, g.First().Title, KnowledgeItemType.Reference))
                                  .ToList(),
            PromptTokens:     result.Usage.PromptTokens,
            CompletionTokens: result.Usage.CompletionTokens,
            Citations:        answer?.Citations
                                  .Select(c => new AnswerCitation(
                                      c.Index, c.DocumentId, c.KnowledgeItemId, c.Title, c.Page, c.Reference))
                                  .ToList(),
            // Le design demande explicitement qu'un agent « refuse de conclure
            // lorsque la source est ambiguë ». Ce champ porte ce refus.
            Unresolved:       answer?.Unresolved);
    }

    /// <summary>
    /// Repli lexical, sans citations et sans modèle.
    ///
    /// Il ne prétend rien : il renvoie les fiches dont le titre ou le contenu
    /// correspond, à charge pour l'appelant de lire. C'est volontairement moins
    /// qu'une réponse — et c'est signalé comme tel.
    /// </summary>
    private async Task<AskResponse> AskLexicallyAsync(string question, Guid? packId, CancellationToken ct)
    {
        var items = await itemRepo.SearchAsync(question, null, KnowledgeItemStatus.Published, packId, 1, 5, ct);

        var extracts = string.Join("\n\n", items.Items.Select(i => $"### {i.Title}\n{i.Content}"));
        var answer = items.Items.Count == 0
            ? "Le service de raisonnement est indisponible et aucune fiche ne correspond à cette question."
            : "Le service de raisonnement est indisponible. Voici les fiches qui correspondent, "
              + "sans synthèse ni citation :\n\n" + extracts;

        return new AskResponse(
            Answer:           answer,
            Sources:          items.Items.Select(i => new SourceRef(i.Id, i.Title, i.Type)).ToList(),
            PromptTokens:     0,
            CompletionTokens: 0);
    }

    /// <summary>Jeton de l'utilisateur réel, relayé au runtime.</summary>
    private string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MISSING_CALLER_TOKEN");

        return header["Bearer ".Length..].Trim();
    }
}
