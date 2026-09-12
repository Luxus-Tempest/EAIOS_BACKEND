using EAIOS.Api.Application.Agent;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Infrastructure.AI;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Agent;
using Microsoft.AspNetCore.Mvc;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Agents IA : CRUD, exécution, mémoire, prompts.
/// </summary>
[Route("api/v1/agents")]
public sealed class AgentsController(
    IAgentService             agentService,
    IAgentRepository          agentRepo,
    IAgentExecutionRepository executionRepo,
    IAgentMemoryRepository    memoryRepo,
    IAgentEvaluationService   evaluation,
    EAIOS.Api.Infrastructure.AI.IAgentRuntimeClient runtime,
    IConfiguration            configuration) : V1ApiController
{
    // ── Agents CRUD ───────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] AgentType? type,
        [FromQuery] AgentStatus? status,
        [FromQuery] AgentVisibility? visibility,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await agentRepo.SearchAsync(q, type, status, visibility, page, pageSize, ct);
        return OkList(result.Items.Select(MapAgent).ToList(), result.TotalCount, page, pageSize);
    }

    [HttpGet("{id:guid}", Name = "GetAgent")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var agent = await agentRepo.GetByIdAsync(id, ct);
        return agent == null ? NotFound() : Ok200(MapAgent(agent));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAgentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var agent = await agentService.CreateAgentAsync(TenantId, req, ActorId.Value, ct);
        return Created201("GetAgent", new { id = agent.Id }, MapAgent(agent));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAgentRequest req, CancellationToken ct)
    {
        try
        {
            var agent = await agentService.UpdateAgentAsync(id, req.DisplayName, req.Description, req.SystemPrompt, req.LlmConfig, req.KnowledgePackIds, req.EnabledTools, req.MemoryEnabled, ct,
                req.Visibility, req.Tags);
            return Ok200(MapAgent(agent));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await agentService.DeleteAgentAsync(id, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Exécution ─────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/execute")]
    public async Task<IActionResult> Execute(Guid id, [FromBody] ExecuteAgentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        
        try
        {
            Guid.TryParse(req.SessionId, out var parsedSessionId);
            var execution = await agentService.ExecuteAsync(TenantId, id, req.Input, ActorId.Value, parsedSessionId == Guid.Empty ? null : parsedSessionId, ct);
            return Ok200(ExecutionViews.Map(execution));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "AGENT_NOT_ACTIVE")
        {
            return UnprocessableEntity("Cet agent n'est pas actif.");
        }
    }

    /// <summary>
    /// Exécution diffusée.
    ///
    /// <para>
    /// Le flux transporte plus que des jetons : la trace d'outils avec leur
    /// durée, le numéro d'étape, le coût courant et l'arrêt pour décision —
    /// c'est ce que montre <c>ConversationAgent</c>. Le backend <b>relaie</b> ces
    /// événements sans les réinterpréter ; leur vocabulaire est fixé par le
    /// runtime.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/execute/stream")]
    public async Task ExecuteStream(Guid id, [FromBody] ExecuteAgentRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) { Response.StatusCode = 401; return; }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        // Sans cela, un proxy inverse tamponnerait la réponse et le flux
        // arriverait d'un bloc à la fin — c'est-à-dire plus du tout un flux.
        Response.Headers.Append("X-Accel-Buffering", "no");

        Guid.TryParse(req.SessionId, out var sessionId);

        await using var writer = new StreamWriter(Response.Body);

        // L'exécution est un fait serveur ; le flux n'en est qu'une fenêtre. Si
        // la fenêtre se ferme (rafraîchissement, onglet fermé), on cesse d'écrire
        // mais on continue de consommer le runtime jusqu'au `done`, pour que le
        // tour soit enregistré avec sa réponse. La consommation n'est bornée que
        // par l'échéance du runtime, pas par la connexion du client.
        var runtimeTimeout = configuration.GetValue("AgentRuntime:TimeoutSeconds", 180);
        using var run = new CancellationTokenSource(TimeSpan.FromSeconds(runtimeTimeout + 30));
        var clientGone = false;

        try
        {
            await foreach (var evt in agentService.ExecuteStreamAsync(
                TenantId, id, req.Input, ActorId.Value,
                sessionId == Guid.Empty ? null : sessionId, run.Token))
            {
                if (clientGone) continue;
                try
                {
                    await writer.WriteAsync($"event: {evt.Event}\ndata: {evt.Data}\n\n");
                    await writer.FlushAsync(ct);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException
                                           || ct.IsCancellationRequested)
                {
                    clientGone = true;
                }
            }
        }
        catch (KeyNotFoundException)
        {
            await WriteErrorEventAsync(writer, "AGENT_NOT_FOUND", "Agent introuvable.", ct);
        }
        catch (InvalidOperationException ex)
        {
            // Les en-têtes sont déjà partis : un code HTTP n'est plus possible.
            // L'échec passe donc par le flux lui-même, sous le même vocabulaire
            // que les erreurs du runtime.
            await WriteErrorEventAsync(writer, ex.Message, ex.Message, ct);
        }
        catch (AgentRuntimeUnavailableException ex)
        {
            await WriteErrorEventAsync(writer, "RUNTIME_UNAVAILABLE", ex.Message, ct);
        }
    }

    private static async Task WriteErrorEventAsync(StreamWriter writer, string code, string message, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        var payload = System.Text.Json.JsonSerializer.Serialize(new { errorCode = code, message });
        try
        {
            await writer.WriteAsync($"event: error\ndata: {payload}\n\n");
            await writer.FlushAsync(ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Le client est parti : l'erreur est déjà consignée sur l'exécution.
        }
    }

    // ── Versions figées ───────────────────────────────────────────────────────
    // Une exécution utilise toujours un instantané : la configuration ne bouge
    // pas sous les pieds d'une conversation en cours, et une reprise après arrêt
    // humain retrouve exactement l'agent qui s'était arrêté.

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAgentRequest? req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            var version = await agentService.PublishAsync(TenantId, id, ActorId.Value, req?.ChangeLog, ct);
            return Ok200(MapVersion(version));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "VERSION_ALREADY_PUBLISHED")
        {
            return Conflict("Cette version est déjà publiée : modifiez l'agent avant de republier.");
        }
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
    {
        var versions = await agentService.ListVersionsAsync(id, ct);
        return Ok200(versions.Select(MapVersion).ToList());
    }

    /// <summary>
    /// Instantané figé d'une version, tel que le runtime d'agents le consomme.
    /// <paramref name="version"/> accepte un numéro, <c>latest</c> ou <c>published</c>.
    /// </summary>
    [HttpGet("{id:guid}/versions/{version}")]
    public async Task<IActionResult> GetVersion(Guid id, string version, CancellationToken ct)
    {
        try
        {
            return Ok200(await agentService.GetVersionSnapshotAsync(id, version, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Version « {version} » introuvable pour cet agent.");
        }
    }

    // ── Jeu de test et évaluations ────────────────────────────────────────────
    // Alimentent le panneau « Santé de l'agent » du Studio : « Tests 50/50 »,
    // « Latence p95 », « Taux d'échec », « Coût / exéc. ».

    [HttpGet("{id:guid}/tests")]
    public async Task<IActionResult> ListTestCases(Guid id, CancellationToken ct)
    {
        var cases = await evaluation.ListTestCasesAsync(id, ct);
        return Ok200(cases.Select(MapTestCase).ToList());
    }

    [HttpPost("{id:guid}/tests")]
    public async Task<IActionResult> CreateTestCase(Guid id, [FromBody] CreateTestCaseRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        var testCase = await evaluation.CreateTestCaseAsync(
            TenantId, id, req.Name, req.Input, ActorId.Value,
            req.ExpectedPhrases, req.ForbiddenPhrases, req.RequiresCitation, ct);

        return Ok200(MapTestCase(testCase));
    }

    [HttpDelete("{id:guid}/tests/{testCaseId:guid}")]
    public async Task<IActionResult> DeleteTestCase(Guid id, Guid testCaseId, CancellationToken ct)
    {
        try
        {
            await evaluation.DeleteTestCaseAsync(id, testCaseId, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Lance une passe complète contre la dernière version figée.
    ///
    /// Les cas s'exécutent en série : les paralléliser saturerait le palier du
    /// fournisseur, et les latences mesurées incluraient l'attente de notre
    /// propre file au lieu du temps de l'agent.
    /// </summary>
    [HttpPost("{id:guid}/tests/run")]
    public async Task<IActionResult> RunTests(Guid id, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        try
        {
            return Ok200(MapTestRun(await evaluation.RunAsync(TenantId, id, ActorId.Value, ct)));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMPTY_TEST_SET")
        {
            return UnprocessableEntity("Le jeu de test est vide : ajoutez au moins un cas.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "AGENT_NOT_PUBLISHED")
        {
            return UnprocessableEntity("Publiez l'agent avant de lancer son jeu de test : une passe s'exécute sur une version figée.");
        }
    }

    [HttpGet("{id:guid}/tests/runs")]
    public async Task<IActionResult> ListTestRuns(Guid id, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var runs = await evaluation.ListRunsAsync(id, limit, ct);
        return Ok200(runs.Select(MapTestRun).ToList());
    }

    [HttpGet("{id:guid}/tests/runs/{runId:guid}")]
    public async Task<IActionResult> GetTestRunResults(Guid id, Guid runId, CancellationToken ct)
    {
        var results = await evaluation.GetResultsAsync(runId, ct);
        return Ok200(results.Select(r => new
        {
            r.Id, r.TestCaseId, r.ExecutionId, r.Passed, r.LatencyMs,
            r.CostUsd, r.TotalTokens, r.CitationCount, r.FailureReason, r.Output
        }).ToList());
    }

    // ── Historique d'exécutions ───────────────────────────────────────────────

    [HttpGet("{id:guid}/executions")]
    public async Task<IActionResult> ListExecutions(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await executionRepo.GetByAgentAsync(id, page, pageSize, ct);
        return OkList(result.Items.Select(ExecutionViews.Map).ToList(), result.TotalCount, page, pageSize);
    }

    // ── Catalogue d'outils ────────────────────────────────────────────────────

    /// <summary>
    /// Les outils que le runtime sait monter, avec leur description et le
    /// marqueur « écrit ». Le Studio s'en sert pour proposer une sélection au
    /// lieu d'une saisie de nom au clavier.
    /// </summary>
    [HttpGet("tools")]
    public async Task<IActionResult> ListTools(CancellationToken ct)
    {
        try
        {
            var tools = await runtime.GetToolCatalogAsync(ct);
            return Ok200(tools);
        }
        catch (AgentRuntimeUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title  = "Runtime d'agents injoignable",
                Detail = ex.Message,
            });
        }
    }

    // ── Mémoire ───────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/memory")]
    public async Task<IActionResult> GetMemory(Guid id, [FromQuery] AgentMemoryType? type, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();
        var memories = await memoryRepo.GetByAgentAsync(id, ActorId.Value, type, ct);
        return Ok200(memories.Select(m => new { m.Id, m.Key, Value = m.Content, m.Type, m.ImportanceScore, m.AccessCount, m.ExpiresAt, m.CreatedAt }).ToList());
    }

    [HttpPost("{id:guid}/memory")]
    public async Task<IActionResult> UpsertMemory(Guid id, [FromBody] UpsertMemoryRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        await agentService.UpsertMemoryAsync(TenantId, id, req.Type, req.Key, req.Value, ActorId.Value, req.ImportanceScore, ct);
        return Ok(new { message = "Mémoire mise à jour." });
    }

    [HttpDelete("{id:guid}/memory/{memoryId:guid}")]
    public async Task<IActionResult> DeleteMemory(Guid id, Guid memoryId, CancellationToken ct)
    {
        try
        {
            await agentService.DeleteMemoryAsync(id, memoryId, ct);
            return NoContent204();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static readonly System.Text.Json.JsonSerializerOptions LlmConfigJson =
        new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>
    /// La configuration d'un agent se lisait comme des constantes (« gpt-4o »,
    /// 0,7, 4096) quelle que soit sa configuration réelle ; le frontend devait
    /// aller la chercher dans l'instantané. Elle est désormais lue là où elle est.
    /// </summary>
    private static object MapAgent(Domain.Agent.Agent a)
    {
        AgentLlmConfig llm;
        try
        {
            llm = System.Text.Json.JsonSerializer.Deserialize<AgentLlmConfig>(
                string.IsNullOrWhiteSpace(a.LlmConfigJson) ? "{}" : a.LlmConfigJson, LlmConfigJson) ?? new AgentLlmConfig();
        }
        catch (System.Text.Json.JsonException)
        {
            llm = new AgentLlmConfig();
        }

        return new
        {
            a.Id, a.Name, a.DisplayName, a.Description, a.Type, a.Status, a.Visibility, a.SystemPrompt,
            LlmProvider = llm.Provider, LlmModel = llm.Model, llm.Temperature, MaxTokens = llm.MaxOutputTokens,
            LlmConfig = new { llm.Provider, llm.Model, llm.Temperature, llm.MaxOutputTokens, llm.UseStreaming, llm.TopP },
            a.KnowledgePackIds, a.EnabledTools, a.MemoryEnabled, a.RequireHumanConfirmation, a.MaxExecutionSeconds,
            a.VersionNumber, a.PublishedVersionId, a.PublishedAt, a.ExecutionCount, a.TotalCostUsd,
            a.OwnerId, a.WorkspaceId, a.Tags, a.CreatedAt, a.UpdatedAt
        };
    }

    private static object MapTestCase(AgentTestCase c) => new
    {
        c.Id, c.AgentId, c.Name, c.Input, c.ExpectedPhrases, c.ForbiddenPhrases,
        c.RequiresCitation, c.IsEnabled, c.CreatedAt
    };

    private static object MapTestRun(AgentTestRun r) => new
    {
        r.Id, r.AgentId, r.AgentVersion, r.Status, r.StartedAt, r.CompletedAt,
        r.TotalCases, r.PassedCases, r.FailedCases,
        // Les quatre chiffres du panneau « Santé de l'agent ».
        r.P95LatencyMs, r.MedianLatencyMs, r.FailureRate, r.AverageCostUsd,
        r.TotalCostUsd, r.TotalTokens, r.ErrorMessage
    };

    private static object MapVersion(AgentVersion v) => new
    {
        v.Id, v.AgentId, v.VersionNumber, v.ChangeLog, v.PublishedBy, v.PublishedAt
    };

}
