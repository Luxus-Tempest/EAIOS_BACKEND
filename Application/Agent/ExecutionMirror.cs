using System.Text.Json;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;

namespace EAIOS.Api.Application.Agent;

/// <summary>
/// Reporte un résultat du runtime sur une <c>AgentExecution</c>.
///
/// <para>
/// L'exécution est un <b>miroir</b> de supervision : la source de l'état
/// conversationnel reste le checkpointer du runtime. Le premier appel et la
/// reprise après décision humaine passent tous deux par ici — un arrêt suivi
/// d'une reprise doit produire exactement les mêmes champs qu'une exécution
/// menée d'un trait.
/// </para>
/// </summary>
public static class ExecutionMirror
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented        = false,
    };

    public static void Apply(AgentExecution execution, RuntimeRunResult result)
    {
        var usage = result.Usage;

        // Une exécution reprise après un arrêt a déjà consommé : le runtime ne
        // compte que la reprise, le miroir additionne. Sans cela, la fiche d'une
        // exécution qui avait demandé une décision affichait « 0 jeton ».
        var priorTokens = execution.TotalTokens;
        var priorCost   = execution.CostUsd;

        if (result.IsFailed)
        {
            execution.RecordUsage(priorTokens + usage.TotalTokens, priorCost + usage.CostUsd, usage.ModelUsed, usage.StepCount);
            execution.Fail(result.ErrorCode ?? "EXECUTION_FAILED", result.ErrorMessage ?? "Échec du runtime.");
            return;
        }

        if (result.IsInterrupted)
        {
            // Un arrêt n'est pas un échec : l'état est checkpointé côté runtime
            // et l'exécution reprendra exactement au point d'arrêt. La charge
            // utile est conservée telle quelle — c'est elle qui deviendra une
            // tâche humaine.
            execution.RecordUsage(priorTokens + usage.TotalTokens, priorCost + usage.CostUsd, usage.ModelUsed, usage.StepCount);
            execution.AwaitHumanInput(
                result.Interrupt is null ? null : JsonSerializer.Serialize(result.Interrupt, Json));
            return;
        }

        var answer = result.Output;

        execution.Complete(
            output:           answer?.Answer,
            promptTokens:     usage.PromptTokens,
            completionTokens: usage.CompletionTokens,
            costUsd:          usage.CostUsd,
            modelUsed:        usage.ModelUsed,
            // Les citations viennent d'une sortie structurée : elles ne sont ni
            // reconstruites ni extraites par expression régulière.
            citations:        answer?.Citations.Select(FormatCitation).ToArray(),
            sourceDocIds:     answer?.Citations.Where(c => c.DocumentId.HasValue)
                                               .Select(c => c.DocumentId!.Value)
                                               .Distinct().ToArray(),
            stepCount:        usage.StepCount,
            outputDataJson:   answer is null ? null : JsonSerializer.Serialize(answer, Json));

        // `Complete` additionne prompt et completion ; le total compté par le
        // runtime (`usage_metadata` cumulé) fait foi quand il est plus grand.
        execution.RecordUsage(
            priorTokens + Math.Max(execution.TotalTokens, usage.TotalTokens),
            priorCost + usage.CostUsd, usage.ModelUsed, usage.StepCount);
    }

    /// <summary>Forme attendue par la conversation : « [1] Contrat-cadre v12 · p. 3, art. 7.1 ».</summary>
    private static string FormatCitation(RuntimeCitation citation)
    {
        var locator = new List<string>();
        if (citation.Page is { } page)           locator.Add($"p. {page}");
        if (citation.Reference is { } reference) locator.Add(reference);

        return locator.Count == 0
            ? $"[{citation.Index}] {citation.Title}"
            : $"[{citation.Index}] {citation.Title} · {string.Join(", ", locator)}";
    }
}
