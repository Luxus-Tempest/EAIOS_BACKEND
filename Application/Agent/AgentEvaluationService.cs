using System.Diagnostics;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using EAIOS.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EAIOS.Api.Application.Agent;

/// <summary>
/// Exécute le jeu de test d'un agent contre une version figée.
///
/// <para>
/// Alimente le panneau « Santé de l'agent » du Studio : « Tests 50/50 »,
/// « Latence p95 », « Taux d'échec », « Coût / exéc. ».
/// </para>
/// </summary>
public interface IAgentEvaluationService
{
    Task<AgentTestRun> RunAsync(Guid tenantId, Guid agentId, Guid actorId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTestRun>> ListRunsAsync(Guid agentId, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTestResult>> GetResultsAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentTestCase>> ListTestCasesAsync(Guid agentId, CancellationToken ct = default);
    Task<AgentTestCase> CreateTestCaseAsync(Guid tenantId, Guid agentId, string name, string input,
        Guid actorId, string[]? expectedPhrases, string[]? forbiddenPhrases, bool requiresCitation,
        CancellationToken ct = default);
    Task DeleteTestCaseAsync(Guid agentId, Guid testCaseId, CancellationToken ct = default);
}

public sealed class AgentEvaluationService(
    EaiosDbContext db,
    IAgentRepository agentRepo,
    IAgentVersionRepository versionRepo,
    IAgentContextService contextService,
    IAgentRuntimeClient runtime,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AgentEvaluationService> logger) : IAgentEvaluationService
{
    public async Task<AgentTestRun> RunAsync(Guid tenantId, Guid agentId, Guid actorId, CancellationToken ct = default)
    {
        var agent = await agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new KeyNotFoundException("Agent introuvable.");

        var version = await versionRepo.FindLatestAsync(agentId, ct)
            ?? throw new InvalidOperationException("AGENT_NOT_PUBLISHED");

        var cases = await db.AgentTestCases
            .Where(c => c.AgentId == agentId && c.IsEnabled)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        if (cases.Count == 0)
            throw new InvalidOperationException("EMPTY_TEST_SET");

        var versionRef = version.VersionNumber.ToString();
        var run = AgentTestRun.Create(tenantId, agentId, versionRef, cases.Count, actorId);

        await db.AgentTestRuns.AddAsync(run, ct);
        await db.SaveChangesAsync(ct);

        var callerToken = CallerToken();
        var latencies   = new List<int>(cases.Count);
        var results     = new List<AgentTestResult>(cases.Count);
        var totalCost   = 0m;
        var totalTokens = 0;

        // Les cas s'exécutent en série, délibérément. Les paralléliser
        // saturerait le palier du fournisseur — le limiteur de débit ferait
        // alors attendre les requêtes, et les latences mesurées incluraient
        // cette attente : on mesurerait sa propre file, pas l'agent.
        foreach (var testCase in cases)
        {
            ct.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var executionId = Guid.CreateVersion7();

            try
            {
                var context = contextService.Issue(tenantId, actorId, agent, versionRef, executionId);
                var result  = await runtime.RunAsync(context.Token, callerToken, testCase.Input, ct);
                stopwatch.Stop();

                var verdict = Evaluate(testCase, result);

                totalCost   += result.Usage.CostUsd;
                totalTokens += result.Usage.TotalTokens;
                latencies.Add((int)stopwatch.ElapsedMilliseconds);

                results.Add(AgentTestResult.Create(
                    tenantId, run.Id, testCase.Id,
                    verdict.Passed, (int)stopwatch.ElapsedMilliseconds,
                    result.Usage.CostUsd, result.Usage.TotalTokens,
                    result.Output?.Answer, result.Output?.Citations.Count ?? 0,
                    verdict.Reason, executionId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stopwatch.Stop();
                latencies.Add((int)stopwatch.ElapsedMilliseconds);

                // Un cas qui plante est un cas qui échoue : ne pas le compter
                // gonflerait artificiellement le taux de réussite.
                results.Add(AgentTestResult.Create(
                    tenantId, run.Id, testCase.Id, passed: false,
                    (int)stopwatch.ElapsedMilliseconds, 0m, 0, null, 0,
                    $"L'exécution a échoué : {ex.Message}", executionId));
            }
        }

        await db.AgentTestResults.AddRangeAsync(results, ct);

        var passed = results.Count(r => r.Passed);
        run.Complete(passed, results.Count - passed, latencies, totalCost, totalTokens);

        db.AgentTestRuns.Update(run);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Jeu de test de l'agent {AgentId} v{Version} : {Passed}/{Total}, p95 {P95} ms, {Cost} USD.",
            agentId, versionRef, passed, results.Count, run.P95LatencyMs, run.TotalCostUsd);

        return run;
    }

    /// <summary>
    /// Verdict d'un cas.
    ///
    /// Déterministe et sans appel de modèle : les assertions portent sur la
    /// présence de phrases et de citations. Le message d'échec nomme ce qui
    /// manque — « assertion failed » ne se corrige pas.
    /// </summary>
    private static (bool Passed, string? Reason) Evaluate(AgentTestCase testCase, RuntimeRunResult result)
    {
        if (result.IsFailed)
            return (false, $"Exécution en échec : {result.ErrorCode}.");

        if (result.IsInterrupted)
            // Un arrêt n'est ni une réussite ni un plantage : c'est un cas de
            // test mal choisi, et le dire évite de chercher un bug ailleurs.
            return (false, "L'agent a demandé une décision humaine : ce cas ne peut pas s'évaluer automatiquement.");

        var answer = result.Output?.Answer ?? "";

        foreach (var expected in testCase.ExpectedPhrases)
        {
            if (!answer.Contains(expected, StringComparison.OrdinalIgnoreCase))
                return (false, $"La phrase attendue « {expected} » est absente de la réponse.");
        }

        foreach (var forbidden in testCase.ForbiddenPhrases)
        {
            if (answer.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                return (false, $"La phrase interdite « {forbidden} » apparaît dans la réponse.");
        }

        if (testCase.RequiresCitation && (result.Output?.Citations.Count ?? 0) == 0)
            return (false, "La réponse ne cite aucune source.");

        return (true, null);
    }

    public async Task<IReadOnlyList<AgentTestRun>> ListRunsAsync(Guid agentId, int limit = 20, CancellationToken ct = default) =>
        await db.AgentTestRuns
            .Where(r => r.AgentId == agentId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AgentTestResult>> GetResultsAsync(Guid runId, CancellationToken ct = default) =>
        await db.AgentTestResults
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AgentTestCase>> ListTestCasesAsync(Guid agentId, CancellationToken ct = default) =>
        await db.AgentTestCases.Where(c => c.AgentId == agentId).OrderBy(c => c.CreatedAt).ToListAsync(ct);

    public async Task<AgentTestCase> CreateTestCaseAsync(Guid tenantId, Guid agentId, string name, string input,
        Guid actorId, string[]? expectedPhrases, string[]? forbiddenPhrases, bool requiresCitation,
        CancellationToken ct = default)
    {
        var testCase = AgentTestCase.Create(
            tenantId, agentId, name, input, actorId, expectedPhrases, forbiddenPhrases, requiresCitation);

        await db.AgentTestCases.AddAsync(testCase, ct);
        await db.SaveChangesAsync(ct);
        return testCase;
    }

    public async Task DeleteTestCaseAsync(Guid agentId, Guid testCaseId, CancellationToken ct = default)
    {
        var testCase = await db.AgentTestCases.FirstOrDefaultAsync(c => c.Id == testCaseId && c.AgentId == agentId, ct)
            ?? throw new KeyNotFoundException("Cas de test introuvable.");

        db.AgentTestCases.Remove(testCase);
        await db.SaveChangesAsync(ct);
    }

    private string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MISSING_CALLER_TOKEN");

        return header["Bearer ".Length..].Trim();
    }
}
