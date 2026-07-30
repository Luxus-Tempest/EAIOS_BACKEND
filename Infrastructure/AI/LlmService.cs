namespace EAIOS.Api.Infrastructure.AI;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface ILlmService
{
    Task<LlmResult> GenerateAsync(string systemPrompt, string userInput, LlmOptions? options = null, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userInput, LlmOptions? options = null, CancellationToken ct = default);
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public sealed record LlmOptions(
    string  Model       = "gpt-4o",
    float   Temperature = 0.7f,
    int     MaxTokens   = 4096,
    float?  TopP        = null,
    bool    JsonMode    = false);

public sealed record LlmResult(
    string  Output,
    int     PromptTokens,
    int     CompletionTokens,
    string  ModelUsed,
    decimal CostUsd,
    bool    Truncated = false)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

// ── Stub (dev / test) ─────────────────────────────────────────────────────────

public sealed class StubLlmService(ILogger<StubLlmService> logger) : ILlmService
{
    public Task<LlmResult> GenerateAsync(string systemPrompt, string userInput, LlmOptions? options = null, CancellationToken ct = default)
    {
        logger.LogInformation("[LLM STUB] Generate for input: {Input}", userInput[..Math.Min(80, userInput.Length)]);
        var result = new LlmResult(
            Output:            $"[EAIOS Stub IA] Réponse générée pour : « {userInput} »\n\nCeci est une réponse de démonstration. Connectez un vrai provider LLM (OpenAI, Azure OpenAI, etc.) en production.",
            PromptTokens:      50,
            CompletionTokens:  120,
            ModelUsed:         options?.Model ?? "stub-gpt-4o",
            CostUsd:           0.002m);
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userInput, LlmOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var words = new[] { "Voici ", "une ", "réponse ", "streamée ", "de ", "démonstration ", "EAIOS." };
        foreach (var word in words)
        {
            if (ct.IsCancellationRequested) yield break;
            await Task.Delay(50, ct);
            yield return word;
        }
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Vecteur déterministe basé sur le hash du texte pour les tests
        var hash   = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        var vector = new float[1536];
        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(hash[i % hash.Length] - 128) / 256f;
        return Task.FromResult(vector);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>(texts.Count);
        foreach (var text in texts)
            results.Add(await EmbedAsync(text, ct));
        return results;
    }
}
