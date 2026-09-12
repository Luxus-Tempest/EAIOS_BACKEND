using EAIOS.Api.Domain.Shared.Primitives;

namespace EAIOS.Api.Domain.Agent;

// ═══════════════════════════════════════════════════════════════════════════════
// JEUX DE TEST ET ÉVALUATIONS
//
// `StudioAgent` montre un panneau « Santé de l'agent » : « Tests 50/50 »,
// « Latence p95 4,2 s », « Taux d'échec 0,4 % », « Coût / exéc. $ 0,64 ». Ces
// trois entités le remplissent.
//
// Pourquoi un harnais local plutôt que LangSmith, qui fournit pourtant
// `evaluate()` nativement : LangSmith envoie les données d'évaluation chez un
// tiers. Les entrées de test d'EAIOS sont des questions sur des contrats et des
// politiques internes, et les sorties citent des documents classifiés. Une
// dépendance qui exfiltre cela est incompatible avec la classification
// `StrictlyConfidential` et les conservations légales. Le traçage LangSmith
// reste branchable pour qui l'accepte, mais il ne peut pas être le seul chemin.
// ═══════════════════════════════════════════════════════════════════════════════

public enum AgentTestRunStatus { Running, Completed, Failed, Cancelled }

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentTestCase
// Table: org_{id}.agent.test_cases
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Un cas du jeu de test d'un agent.
///
/// <para>
/// Les assertions sont <b>déterministes</b> — présence de phrases attendues,
/// absence de phrases interdites, présence d'au moins une citation. Aucun juge
/// LLM : un juge coûte un appel de modèle par cas, introduit sa propre variance,
/// et rend un jeu de cinquante tests plus cher et moins reproductible que ce
/// qu'il mesure. Un juge pourra s'ajouter, il ne peut pas être le socle.
/// </para>
/// </summary>
public sealed class AgentTestCase : TenantEntity
{
    public Guid AgentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Input { get; private set; } = string.Empty;

    /// <summary>Toutes doivent apparaître dans la réponse, sans tenir compte de la casse.</summary>
    public string[] ExpectedPhrases { get; private set; } = [];

    /// <summary>Aucune ne doit apparaître. Sert à interdire une formulation connue pour être fausse.</summary>
    public string[] ForbiddenPhrases { get; private set; } = [];

    /// <summary>
    /// La réponse doit citer au moins une source.
    ///
    /// C'est l'assertion la plus utile d'une base documentaire : une réponse
    /// juste mais non sourcée reste inopposable.
    /// </summary>
    public bool RequiresCitation { get; private set; } = true;

    public bool IsEnabled { get; private set; } = true;

    public static AgentTestCase Create(Guid organizationId, Guid agentId, string name, string input,
        Guid createdBy, string[]? expectedPhrases = null, string[]? forbiddenPhrases = null,
        bool requiresCitation = true)
    {
        var testCase = new AgentTestCase
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            Name = name.Trim(),
            Input = input,
            ExpectedPhrases = expectedPhrases ?? [],
            ForbiddenPhrases = forbiddenPhrases ?? [],
            RequiresCitation = requiresCitation
        };
        testCase.SetOrganizationId(organizationId);
        testCase.SetCreated(createdBy);
        return testCase;
    }

    public void Update(string? name, string? input, string[]? expectedPhrases,
        string[]? forbiddenPhrases, bool? requiresCitation, bool? isEnabled)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (input is not null) Input = input;
        if (expectedPhrases is not null) ExpectedPhrases = expectedPhrases;
        if (forbiddenPhrases is not null) ForbiddenPhrases = forbiddenPhrases;
        if (requiresCitation.HasValue) RequiresCitation = requiresCitation.Value;
        if (isEnabled.HasValue) IsEnabled = isEnabled.Value;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentTestRun
// Table: org_{id}.agent.test_runs
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Une passe complète du jeu de test contre une version figée.
///
/// La version est enregistrée : comparer deux passes n'a de sens que si l'on
/// sait ce qui a changé entre elles. C'est ce que montre le Studio avec
/// « Écart avec v2.1.0 ».
/// </summary>
public sealed class AgentTestRun : TenantEntity
{
    public Guid AgentId { get; private set; }
    public string AgentVersion { get; private set; } = string.Empty;
    public AgentTestRunStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public Guid TriggeredBy { get; private set; }

    public int TotalCases { get; private set; }
    public int PassedCases { get; private set; }
    public int FailedCases { get; private set; }

    /// <summary>Latence au 95e centile, en millisecondes — « Latence p95 4,2 s ».</summary>
    public int? P95LatencyMs { get; private set; }
    public int? MedianLatencyMs { get; private set; }
    public decimal TotalCostUsd { get; private set; }
    public int TotalTokens { get; private set; }
    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<AgentTestResult> Results { get; private set; } = new List<AgentTestResult>();

    /// <summary>Taux d'échec, en pourcentage — « Taux d'échec 0,4 % ».</summary>
    public decimal FailureRate => TotalCases == 0 ? 0m : Math.Round(100m * FailedCases / TotalCases, 2);

    /// <summary>Coût moyen par exécution — « Coût / exéc. $ 0,64 ».</summary>
    public decimal AverageCostUsd => TotalCases == 0 ? 0m : Math.Round(TotalCostUsd / TotalCases, 4);

    public static AgentTestRun Create(Guid organizationId, Guid agentId, string agentVersion,
        int totalCases, Guid triggeredBy)
    {
        var run = new AgentTestRun
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            AgentVersion = agentVersion,
            Status = AgentTestRunStatus.Running,
            StartedAt = DateTime.UtcNow,
            TotalCases = totalCases,
            TriggeredBy = triggeredBy
        };
        run.SetOrganizationId(organizationId);
        run.SetCreated(triggeredBy);
        return run;
    }

    public void Complete(int passed, int failed, IReadOnlyList<int> latencies,
        decimal totalCostUsd, int totalTokens)
    {
        Status = AgentTestRunStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        PassedCases = passed;
        FailedCases = failed;
        TotalCostUsd = totalCostUsd;
        TotalTokens = totalTokens;

        if (latencies.Count > 0)
        {
            var sorted = latencies.OrderBy(l => l).ToArray();
            MedianLatencyMs = sorted[sorted.Length / 2];
            // Centile par rang, borné au dernier index : sur un jeu de dix cas,
            // le p95 est le plus lent — c'est ce qu'on veut savoir, pas une
            // interpolation qui lisserait justement le pic.
            P95LatencyMs = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(0.95 * sorted.Length) - 1)];
        }
    }

    public void Fail(string message)
    {
        Status = AgentTestRunStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = message;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTITY: AgentTestResult
// Table: org_{id}.agent.test_results
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class AgentTestResult : TenantEntity
{
    public Guid RunId { get; private set; }
    public Guid TestCaseId { get; private set; }
    public Guid? ExecutionId { get; private set; }
    public bool Passed { get; private set; }
    public int LatencyMs { get; private set; }
    public decimal CostUsd { get; private set; }
    public int TotalTokens { get; private set; }
    public string? Output { get; private set; }
    public int CitationCount { get; private set; }

    /// <summary>
    /// Pourquoi le cas a échoué, en français et en clair.
    ///
    /// « la phrase attendue « 90 jours » est absente » se corrige ; « assertion
    /// failed » ne se corrige pas.
    /// </summary>
    public string? FailureReason { get; private set; }

    public static AgentTestResult Create(Guid organizationId, Guid runId, Guid testCaseId,
        bool passed, int latencyMs, decimal costUsd, int totalTokens, string? output,
        int citationCount, string? failureReason, Guid? executionId)
    {
        var result = new AgentTestResult
        {
            Id = Guid.CreateVersion7(),
            RunId = runId,
            TestCaseId = testCaseId,
            ExecutionId = executionId,
            Passed = passed,
            LatencyMs = latencyMs,
            CostUsd = costUsd,
            TotalTokens = totalTokens,
            // Un extrait suffit : la sortie complète d'un agent peut peser
            // plusieurs milliers de caractères, multipliés par cinquante cas et
            // par passe.
            Output = output?[..Math.Min(2000, output.Length)],
            CitationCount = citationCount,
            FailureReason = failureReason
        };
        result.SetOrganizationId(organizationId);
        result.SetCreated(null);
        return result;
    }
}
