using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EAIOS.Api.Infrastructure.AI;

/// <summary>
/// Client LLM compatible OpenAI (API OpenAI, Azure OpenAI, ou toute passerelle
/// exposant <c>/chat/completions</c> et <c>/embeddings</c>).
///
/// Complète <see cref="StubLlmService"/>, qui restait jusqu'ici la seule
/// implémentation : le réglage <c>Ai:Provider</c> n'avait donc aucun effet et
/// aucune fonctionnalité IA ne pouvait réellement fonctionner en production.
/// </summary>
public sealed class OpenAiLlmService : ILlmService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiLlmService> _logger;

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _defaultModel;
    private readonly string _embeddingModel;
    private readonly int    _embeddingDimensions;
    private readonly string? _organization;

    /// <summary>Tarifs USD par million de tokens, pour estimer le coût d'une exécution.</summary>
    private static readonly Dictionary<string, (decimal Input, decimal Output)> PricingPerMillionTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4o"]          = (2.50m, 10.00m),
            ["gpt-4o-mini"]     = (0.15m,  0.60m),
            ["gpt-4.1"]         = (2.00m,  8.00m),
            ["gpt-4.1-mini"]    = (0.40m,  1.60m),
            ["gpt-4-turbo"]     = (10.00m, 30.00m),
            ["gpt-3.5-turbo"]   = (0.50m,  1.50m),
        };

    public OpenAiLlmService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OpenAiLlmService> logger)
    {
        _httpClientFactory   = httpClientFactory;
        _logger              = logger;
        _apiKey              = configuration["Ai:ApiKey"] ?? "";
        _baseUrl             = (configuration["Ai:BaseUrl"] ?? "https://api.openai.com/v1").TrimEnd('/');
        _defaultModel        = configuration["Ai:DefaultModel"] ?? "gpt-4o";
        _embeddingModel      = configuration["Ai:DefaultEmbeddingModel"] ?? "text-embedding-3-large";
        _embeddingDimensions = configuration.GetValue("Ai:EmbeddingDimensions", 3072);
        _organization        = configuration["Ai:Organization"];

        if (string.IsNullOrWhiteSpace(_apiKey))
            _logger.LogWarning(
                "Ai:Provider est configuré sur OpenAI mais Ai:ApiKey est vide — les appels LLM échoueront.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GÉNÉRATION
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<LlmResult> GenerateAsync(
        string systemPrompt, string userInput, LlmOptions? options = null, CancellationToken ct = default)
    {
        var model   = options?.Model ?? _defaultModel;
        var request = BuildChatRequest(systemPrompt, userInput, options, model, stream: false);

        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync($"{_baseUrl}/chat/completions", request, JsonOpts, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Appel LLM échoué ({Status}) : {Detail}", (int)response.StatusCode, Truncate(detail, 500));
            throw new InvalidOperationException(
                $"Le fournisseur LLM a répondu {(int)response.StatusCode}. {Truncate(detail, 200)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Réponse LLM vide ou illisible.");

        var choice  = payload.Choices?.FirstOrDefault();
        var content = choice?.Message?.Content ?? string.Empty;

        var promptTokens     = payload.Usage?.PromptTokens ?? 0;
        var completionTokens = payload.Usage?.CompletionTokens ?? 0;

        return new LlmResult(
            Output:           content,
            PromptTokens:     promptTokens,
            CompletionTokens: completionTokens,
            ModelUsed:        payload.Model ?? model,
            CostUsd:          EstimateCost(payload.Model ?? model, promptTokens, completionTokens),
            Truncated:        string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Diffuse la réponse token par token (Server-Sent Events).</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt, string userInput, LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model   = options?.Model ?? _defaultModel;
        var request = BuildChatRequest(systemPrompt, userInput, options, model, stream: true);

        using var client = CreateClient();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOpts)
        };

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Streaming LLM échoué ({Status}) : {Detail}", (int)response.StatusCode, Truncate(detail, 500));
            throw new InvalidOperationException(
                $"Le fournisseur LLM a répondu {(int)response.StatusCode}. {Truncate(detail, 200)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                delta = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta")
                    .TryGetProperty("content", out var c) ? c.GetString() : null;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
            {
                // Fragment de keep-alive ou trame partielle : on l'ignore plutôt
                // que d'interrompre tout le flux.
                continue;
            }

            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EMBEDDINGS
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await EmbedBatchAsync([text], ct);
        return result.Count > 0 ? result[0] : [];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        // L'API rejette les entrées vides : on substitue un espace et on
        // conserve l'alignement 1:1 attendu par l'appelant.
        var inputs = texts.Select(t => string.IsNullOrWhiteSpace(t) ? " " : t).ToArray();

        var request = new EmbeddingRequest
        {
            Model      = _embeddingModel,
            Input      = inputs,
            Dimensions = _embeddingDimensions > 0 ? _embeddingDimensions : null
        };

        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync($"{_baseUrl}/embeddings", request, JsonOpts, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Génération d'embeddings échouée ({Status}) : {Detail}",
                (int)response.StatusCode, Truncate(detail, 500));
            throw new InvalidOperationException(
                $"Le fournisseur d'embeddings a répondu {(int)response.StatusCode}. {Truncate(detail, 200)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Réponse d'embeddings vide ou illisible.");

        // L'ordre de retour n'est pas garanti : on réordonne sur l'index.
        return (payload.Data ?? [])
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding ?? [])
            .ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("LlmClient");
        client.Timeout = TimeSpan.FromMinutes(5);   // les générations longues dépassent le défaut de 100 s
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        if (!string.IsNullOrWhiteSpace(_organization))
            client.DefaultRequestHeaders.Add("OpenAI-Organization", _organization);

        return client;
    }

    private static ChatCompletionRequest BuildChatRequest(
        string systemPrompt, string userInput, LlmOptions? options, string model, bool stream) =>
        new()
        {
            Model    = model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user",   Content = userInput }
            ],
            Temperature    = options?.Temperature ?? 0.7f,
            MaxTokens      = options?.MaxTokens   ?? 4096,
            TopP           = options?.TopP,
            Stream         = stream,
            ResponseFormat = options?.JsonMode == true ? new ResponseFormat { Type = "json_object" } : null
        };

    /// <summary>
    /// Estime le coût d'après la grille tarifaire connue. Un modèle absent de la
    /// grille renvoie 0 plutôt qu'un chiffre inventé.
    /// </summary>
    private static decimal EstimateCost(string model, int promptTokens, int completionTokens)
    {
        var key = PricingPerMillionTokens.Keys
            .FirstOrDefault(k => model.StartsWith(k, StringComparison.OrdinalIgnoreCase));

        if (key is null) return 0m;

        var (input, output) = PricingPerMillionTokens[key];
        return (promptTokens / 1_000_000m * input) + (completionTokens / 1_000_000m * output);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Contrats de transport ─────────────────────────────────────────────────

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = "";
        public List<ChatMessage> Messages { get; set; } = [];
        public float Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("top_p")]      public float? TopP { get; set; }
        public bool Stream { get; set; }
        [JsonPropertyName("response_format")] public ResponseFormat? ResponseFormat { get; set; }
    }

    private sealed class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private sealed class ResponseFormat
    {
        public string Type { get; set; } = "text";
    }

    private sealed class ChatCompletionResponse
    {
        public string? Model { get; set; }
        public List<Choice>? Choices { get; set; }
        public Usage? Usage { get; set; }
    }

    private sealed class Choice
    {
        public ChatMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")]     public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")]      public int TotalTokens { get; set; }
    }

    private sealed class EmbeddingRequest
    {
        public string Model { get; set; } = "";
        public string[] Input { get; set; } = [];
        public int? Dimensions { get; set; }
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingData>? Data { get; set; }
        public Usage? Usage { get; set; }
    }

    private sealed class EmbeddingData
    {
        public int Index { get; set; }
        public float[]? Embedding { get; set; }
    }
}
