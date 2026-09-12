using System.Text.Json;
using EAIOS.Api.Domain.Agent;

namespace EAIOS.Api.Application.Agent;

// ═══════════════════════════════════════════════════════════════════════════════
// Formes de lecture d'une exécution et d'un fil de conversation.
//
// Une seule forme pour l'exécution, servie par `/executions/{id}`,
// `/agents/{id}/executions` et les tours d'un fil : le frontend ne doit pas
// avoir à réconcilier trois formes d'un même objet.
// ═══════════════════════════════════════════════════════════════════════════════

public sealed record AgentExecutionView(
    Guid Id,
    Guid AgentId,
    string AgentVersion,
    Guid? UserId,
    Guid? SessionId,
    AgentExecutionStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    double? DurationMs,
    string? InputText,
    string? OutputText,
    /// <summary>« [1] Contrat-cadre v12 · p. 3, art. 7.1 » — la forme de la conversation.</summary>
    string[] Citations,
    Guid[] SourceDocumentIds,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal CostUsd,
    string? ModelUsed,
    int? StepCount,
    string? ErrorCode,
    string? ErrorMessage,
    bool RequiresHumanInput,
    /// <summary>
    /// La réponse structurée complète (citations avec identifiants, points non
    /// tranchés, confiance) quand l'exécution a abouti ; la demande de décision
    /// quand elle attend une personne. Tel que le runtime l'a produit.
    /// </summary>
    JsonElement? Output,
    JsonElement? Decision);

public sealed record AgentConversationView(
    Guid Id,
    Guid AgentId,
    string? AgentName,
    Guid UserId,
    string Title,
    bool IsPinned,
    int TurnCount,
    DateTime LastActivityAt,
    AgentExecutionStatus LastStatus,
    Guid? LastExecutionId,
    DateTime CreatedAt);

public sealed record ConversationDetailView(
    AgentConversationView Conversation,
    IReadOnlyList<AgentExecutionView> Turns);

public sealed record UpdateConversationRequest(string? Title = null, bool? IsPinned = null);

public static class ExecutionViews
{
    public static AgentExecutionView Map(AgentExecution e)
    {
        JsonElement? payload = TryParse(e.OutputDataJson);
        var awaiting = e.Status == AgentExecutionStatus.AwaitingHumanInput;

        return new AgentExecutionView(
            e.Id, e.AgentId, e.AgentVersion, e.UserId, e.SessionId, e.Status,
            e.StartedAt, e.CompletedAt, e.Duration?.TotalMilliseconds,
            e.InputText, e.OutputText,
            e.Citations ?? [], e.SourceDocumentIds,
            e.PromptTokens, e.CompletionTokens, e.TotalTokens, e.CostUsd,
            e.ModelUsed, e.StepCount, e.ErrorCode, e.ErrorMessage, e.RequiresHumanInput,
            Output:   awaiting ? null : payload,
            Decision: awaiting ? payload : null);
    }

    public static AgentConversationView Map(AgentConversation c, string? agentName = null) => new(
        c.Id, c.AgentId, agentName, c.UserId, c.Title, c.IsPinned, c.TurnCount,
        c.LastActivityAt, c.LastStatus, c.LastExecutionId, c.CreatedAt);

    private static JsonElement? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
