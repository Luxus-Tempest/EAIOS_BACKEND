using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EAIOS.Api.Application.Agent;

namespace EAIOS.Api.Infrastructure.AI;

/// <summary>
/// Client du runtime d'agents (agent-runtime, Python).
///
/// <para>
/// Deux en-têtes accompagnent chaque appel, et ils disent tout de la frontière :
/// <c>X-Eaios-Execution-Context</c> porte la portée signée — le plafond de droits
/// que le runtime applique sans jamais l'élargir ; <c>Authorization</c> relaie le
/// jeton de l'<b>utilisateur réel</b>, pour que toute écriture que l'agent
/// proposerait repasse par cette API sous son identité.
/// </para>
/// <para>
/// Le runtime n'est jamais joignable depuis l'extérieur ni depuis le frontend :
/// ce client est son unique appelant.
/// </para>
/// </summary>
public interface IAgentRuntimeClient
{
    Task<RuntimeRunResult> RunAsync(
        string contextToken, string userToken, string input, CancellationToken ct = default);

    Task<RuntimeRunResult> ResumeAsync(
        string contextToken, string userToken, Guid executionId,
        RuntimeDecisionOutcome outcome, CancellationToken ct = default);

    /// <summary>
    /// Diffuse l'exécution. Les événements arrivent dans l'ordre où le runtime
    /// les produit, et le dernier — <c>done</c> — porte le résultat complet :
    /// c'est lui qui permet de mettre l'exécution à jour après diffusion.
    /// </summary>
    IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
        string contextToken, string userToken, string input, CancellationToken ct = default);

    /// <summary>
    /// Interrogation documentaire par l'assistant intégré, en lecture seule.
    /// Sert les endpoints historiques <c>/knowledge/ask</c> et <c>/search/ask</c>.
    /// </summary>
    Task<RuntimeRunResult> AskAsync(
        string contextToken, string userToken, string question, CancellationToken ct = default);

    Task<IReadOnlyList<RuntimeToolDescriptor>> GetToolCatalogAsync(CancellationToken ct = default);

    /// <summary>
    /// Demande au runtime de resynchroniser les vecteurs d'une organisation
    /// avec ses segments publiés. Appelé après toute écriture de connaissance ;
    /// le runtime traite en tâche de fond et répond tout de suite.
    /// </summary>
    Task ReindexAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Recherche sémantique dans le périmètre de la portée signée. Sert la
    /// recherche hybride du backend ; les résultats sont des segments, avec
    /// leur fiche, leur document et leur page.
    /// </summary>
    Task<IReadOnlyList<RuntimeSearchHit>> SearchAsync(
        string contextToken, string userToken, string query, int topK, CancellationToken ct = default);
}

public sealed record RuntimeSearchHit(
    Guid? ItemId,
    Guid? DocumentId,
    Guid? ChunkId,
    string Title,
    string Excerpt,
    int? Page,
    string? Reference,
    float Score);

/// <summary>
/// Un événement du flux, relayé tel quel au frontend.
///
/// Le vocabulaire est fixé par le runtime — <c>token</c>, <c>tool_start</c>,
/// <c>tool_end</c>, <c>step</c>, <c>interrupt</c>, <c>usage</c>, <c>error</c>,
/// <c>done</c> — et le backend ne le réinterprète pas : il relaie, et n'ouvre
/// que le <c>done</c> final pour consigner l'exécution.
/// </summary>
public sealed record RuntimeStreamEvent(string Event, string Data);

// ── Formes renvoyées par le runtime ──────────────────────────────────────────
// Elles reflètent `agent-runtime/src/eaios_agents/schemas.py`. Les noms sont en
// camelCase parce que FastAPI sérialise les champs Python tels quels et que le
// désérialiseur est configuré insensible à la casse.

public sealed record RuntimeRunResult(
    string Status,
    RuntimeAnswer? Output,
    RuntimeDecisionRequest? Interrupt,
    RuntimeUsage Usage,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsInterrupted => Status == "interrupted";
    public bool IsFailed      => Status == "failed";
}

public sealed record RuntimeAnswer(
    string Answer,
    IReadOnlyList<RuntimeCitation> Citations,
    IReadOnlyList<string> Unresolved,
    float Confidence);

public sealed record RuntimeCitation(
    int Index,
    Guid? DocumentId,
    Guid? KnowledgeItemId,
    string Title,
    int? Page,
    string? Reference,
    string? Excerpt);

public sealed record RuntimeUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal CostUsd,
    string? ModelUsed,
    int StepCount);

public sealed record RuntimeDecisionRequest(
    string Kind,
    string Title,
    string Question,
    IReadOnlyList<RuntimeDecisionOption> Options,
    string? ProposedTool,
    string? Rationale);

public sealed record RuntimeDecisionOption(string Value, string Label, bool IsDestructive);

/// <summary>
/// Ce que le backend renvoie dans <c>Command(resume=…)</c>. Les noms sont en
/// snake_case parce que <c>DecisionOutcome</c> (Pydantic) les valide champ par
/// champ côté Python : sérialisés en PascalCase, la reprise était refusée
/// (422) et chaque décision humaine finissait en « échec ».
/// </summary>
public sealed record RuntimeDecisionOutcome(
    [property: JsonPropertyName("decision")]   string  Decision,
    [property: JsonPropertyName("comment")]    string? Comment,
    [property: JsonPropertyName("decided_by")] Guid    DecidedBy);

public sealed record RuntimeToolDescriptor(string Name, string Description, bool Writes);

/// <summary>Le runtime est injoignable ou a échoué. Distinct d'un refus métier.</summary>
public sealed class AgentRuntimeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class AgentRuntimeClient(HttpClient http, IConfiguration configuration) : IAgentRuntimeClient
{
    public const string ContextHeader = "X-Eaios-Execution-Context";

    /// <summary>
    /// Clé partagée des appels internes sans exécution (réindexation). Distincte
    /// de la clé de signature des portées : un secret par frontière.
    /// </summary>
    public const string InternalKeyHeader = "X-Eaios-Internal-Key";

    private readonly string internalKey = configuration["AgentRuntime:InternalKey"] ?? "";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<RuntimeRunResult> RunAsync(
        string contextToken, string userToken, string input, CancellationToken ct = default)
    {
        // `stream: false` : `AgentService.ExecuteAsync` doit rendre un
        // `AgentExecution` complet à son appelant. Le flux SSE sert la
        // conversation interactive, sur le même graphe et le même fil.
        using var request = Build(HttpMethod.Post, "runs", contextToken, userToken,
            new { input, stream = false });

        return await SendAsync(request, ct);
    }

    public async Task<RuntimeRunResult> ResumeAsync(
        string contextToken, string userToken, Guid executionId,
        RuntimeDecisionOutcome outcome, CancellationToken ct = default)
    {
        using var request = Build(HttpMethod.Post, $"runs/{executionId}/resume",
            contextToken, userToken, new { outcome, stream = false });

        return await SendAsync(request, ct);
    }

    public async Task<RuntimeRunResult> AskAsync(
        string contextToken, string userToken, string question, CancellationToken ct = default)
    {
        using var request = Build(HttpMethod.Post, "ask", contextToken, userToken, new { question });
        return await SendAsync(request, ct);
    }

    public async IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
        string contextToken, string userToken, string input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = Build(HttpMethod.Post, "runs", contextToken, userToken,
            new { input, stream = true });

        HttpResponseMessage response;
        try
        {
            // `ResponseHeadersRead` : sans cela, HttpClient attendrait la fin du
            // corps avant de rendre la main — ce qui annule tout l'intérêt d'un
            // flux.
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AgentRuntimeUnavailableException("Runtime d'agents injoignable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(ct);
                var error   = TryReadError(payload);
                yield return new RuntimeStreamEvent("error", JsonSerializer.Serialize(new
                {
                    errorCode = error.Code ?? $"RUNTIME_HTTP_{(int)response.StatusCode}",
                    message   = error.Message ?? payload[..Math.Min(400, payload.Length)],
                }, Json));
                yield break;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(body);

            // Analyse SSE minimale : `event:` puis `data:`, un bloc se termine
            // sur une ligne vide. On ne dépend pas d'une bibliothèque pour trois
            // règles, et le format est figé par la spécification.
            string? eventName = null;
            var data = new StringBuilder();

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                if (line.Length == 0)
                {
                    if (data.Length > 0)
                        yield return new RuntimeStreamEvent(eventName ?? "message", data.ToString());

                    eventName = null;
                    data.Clear();
                    continue;
                }

                if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventName = line[6..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                    data.Append(line[5..].Trim());
            }

            // Un flux interrompu sans ligne vide finale ne doit pas perdre son
            // dernier bloc — c'est justement le `done`.
            if (data.Length > 0)
                yield return new RuntimeStreamEvent(eventName ?? "message", data.ToString());
        }
    }

    public async Task ReindexAsync(Guid organizationId, CancellationToken ct = default)
    {
        // Appel interne, sans exécution : la clé partagée tient lieu de portée.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"index/{organizationId}");
        request.Headers.Add(InternalKeyHeader, internalKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AgentRuntimeUnavailableException("Runtime d'agents injoignable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AgentRuntimeUnavailableException(
                    $"Réindexation refusée par le runtime ({(int)response.StatusCode}).");
        }
    }

    public async Task<IReadOnlyList<RuntimeSearchHit>> SearchAsync(
        string contextToken, string userToken, string query, int topK, CancellationToken ct = default)
    {
        using var request = Build(HttpMethod.Post, "search", contextToken, userToken, new { query, top_k = topK });

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AgentRuntimeUnavailableException("Runtime d'agents injoignable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AgentRuntimeUnavailableException($"Recherche refusée par le runtime ({(int)response.StatusCode}).");

            var hits = await response.Content.ReadFromJsonAsync<List<RuntimeSearchHitPayload>>(Json, ct) ?? [];
            return hits.Select(h => new RuntimeSearchHit(
                h.item_id, h.document_id, h.chunk_id, h.title ?? "", h.excerpt ?? "", h.page, h.reference, h.score)).ToList();
        }
    }

    /// <summary>Forme brute du runtime (snake_case), traduite avant de sortir de ce client.</summary>
    private sealed record RuntimeSearchHitPayload(
        Guid? item_id, Guid? document_id, Guid? chunk_id, string? title, string? excerpt,
        int? page, string? reference, float score);

    public async Task<IReadOnlyList<RuntimeToolDescriptor>> GetToolCatalogAsync(CancellationToken ct = default)
    {
        // Le catalogue ne dépend d'aucune exécution : pas de portée à signer.
        // C'est lui qui peuplera l'onglet « Outils » du Studio d'agent, sans que
        // le frontend tienne une liste parallèle.
        try
        {
            var tools = await http.GetFromJsonAsync<List<RuntimeToolDescriptor>>("tools", Json, ct);
            return tools ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AgentRuntimeUnavailableException("Runtime d'agents injoignable.", ex);
        }
    }

    private static HttpRequestMessage Build(
        HttpMethod method, string path, string contextToken, string userToken, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        request.Headers.Add(ContextHeader, contextToken);
        return request;
    }

    private async Task<RuntimeRunResult> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AgentRuntimeUnavailableException("Runtime d'agents injoignable.", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<RuntimeRunResult>(payload, Json)
                    ?? throw new AgentRuntimeUnavailableException("Réponse du runtime illisible.");
            }

            // Le runtime traduit ses échecs en codes stables — CONTEXT_REJECTED,
            // BUDGET_EXCEEDED, UNKNOWN_TOOL. Ils atterrissent tels quels dans
            // `AgentExecution.ErrorCode` : ce sont des valeurs de contrat, pas
            // des messages de confort.
            var error = TryReadError(payload);
            return new RuntimeRunResult(
                "failed", null, null,
                new RuntimeUsage(0, 0, 0, 0m, null, 0),
                error.Code ?? $"RUNTIME_HTTP_{(int)response.StatusCode}",
                error.Message ?? payload[..Math.Min(400, payload.Length)]);
        }
    }

    private static (string? Code, string? Message) TryReadError(string payload)
    {
        try
        {
            var root = JsonDocument.Parse(payload).RootElement;
            return (
                root.TryGetProperty("errorCode", out var code) ? code.GetString() : null,
                root.TryGetProperty("message", out var message) ? message.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
