using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Security;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EAIOS.Api.Application.Connector;

public sealed class ConnectorService(
    IConnectorInstanceRepository instanceRepo,
    IConnectorDefinitionRepository definitionRepo,
    ISyncJobRepository syncJobRepo,
    ICredentialProtector credentialProtector,
    IHttpClientFactory httpClientFactory,
    ILogger<ConnectorService> logger) : IConnectorService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    // ═════════════════════════════════════════════════════════════════════════
    // INSTANCES
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<ConnectorInstance> CreateInstanceAsync(
        Guid tenantId, Guid definitionId, string name, string? description,
        Guid? workspaceId, Guid actorId, CancellationToken ct = default) =>
        await CreateInstanceAsync(tenantId, definitionId, name, description, workspaceId, actorId, null, null, ct);

    /// <summary>
    /// Crée une instance de connecteur. La configuration est stockée en clair
    /// (elle est relue par l'UI) tandis que les identifiants sont chiffrés :
    /// ils ne doivent jamais ressortir de la base en texte lisible.
    /// </summary>
    public async Task<ConnectorInstance> CreateInstanceAsync(
        Guid tenantId, Guid definitionId, string name, string? description,
        Guid? workspaceId, Guid actorId,
        Dictionary<string, string>? configuration,
        Dictionary<string, string>? credentials,
        CancellationToken ct = default)
    {
        var definition = await definitionRepo.GetByIdAsync(definitionId, ct)
            ?? throw new KeyNotFoundException("Définition de connecteur introuvable dans le catalogue.");

        if (!definition.IsActive)
            throw new InvalidOperationException($"Le connecteur « {definition.Name} » n'est plus disponible.");

        var configJson = configuration is null ? "{}" : JsonSerializer.Serialize(configuration);
        var encrypted  = credentials is { Count: > 0 } ? credentialProtector.Protect(credentials) : null;

        var instance = ConnectorInstance.Create(
            tenantId, definitionId, name, actorId,
            description: description, workspaceId: workspaceId,
            configJson: configJson, credentialsEncrypted: encrypted);

        // Une instance n'est activable qu'une fois sa configuration complète.
        var missing = FindMissingRequiredFields(definition, configuration, credentials);
        if (missing.Count == 0)
            instance.Activate();
        else
            logger.LogInformation("Instance {Name} créée en configuration — champs manquants : {Missing}",
                name, string.Join(", ", missing));

        await instanceRepo.AddAsync(instance, ct);
        await instanceRepo.SaveAsync(ct);

        return instance;
    }

    public async Task<ConnectorInstance> UpdateInstanceAsync(
        Guid id, string? name, string? description, CancellationToken ct = default) =>
        await UpdateInstanceAsync(id, name, description, null, null, ct);

    public async Task<ConnectorInstance> UpdateInstanceAsync(
        Guid id, string? name, string? description,
        Dictionary<string, string>? configuration,
        Dictionary<string, string>? credentials,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");

        instance.UpdateMetadata(name ?? instance.Name, description);

        if (configuration is not null)
            instance.UpdateConfig(JsonSerializer.Serialize(configuration));

        // Des identifiants absents de la requête ne doivent pas effacer ceux en place.
        if (credentials is { Count: > 0 })
            instance.UpdateCredentials(credentialProtector.Protect(credentials));

        instanceRepo.Update(instance);
        await instanceRepo.SaveAsync(ct);

        return instance;
    }

    public async Task DeleteInstanceAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");

        // Les jobs rattachés doivent cesser d'être planifiés avec l'instance.
        var jobs = await syncJobRepo.GetByInstanceAsync(id, ct);
        foreach (var job in jobs)
            syncJobRepo.SoftDelete(job);

        instanceRepo.SoftDelete(instance);
        await syncJobRepo.SaveAsync(ct);
        await instanceRepo.SaveAsync(ct);

        logger.LogInformation("Connecteur {InstanceId} supprimé ({Count} job(s) associé(s)).", id, jobs.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TEST DE CONNEXION
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Teste réellement la connexion : vérifie d'abord que la configuration
    /// obligatoire est complète, puis, si un point d'entrée HTTP est configuré,
    /// interroge celui-ci avec les identifiants stockés et mesure la latence.
    /// Le résultat met à jour l'état de santé de l'instance.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");

        var definition = await definitionRepo.GetByIdAsync(instance.DefinitionId, ct)
            ?? throw new KeyNotFoundException("Définition de connecteur introuvable.");

        var config      = DeserializeConfig(instance.ConfigurationJson);
        var credentials = credentialProtector.Unprotect(instance.CredentialsEncrypted);

        // ── 1. Complétude de la configuration ─────────────────────────────────
        var missing = FindMissingRequiredFields(definition, config, credentials);
        if (missing.Count > 0)
        {
            var message = $"Configuration incomplète — champs requis manquants : {string.Join(", ", missing)}.";
            instance.SetError(message);
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);

            return new ConnectionTestResult(false, 0, 0, [], message);
        }

        // ── 2. Sonde réseau, si un point d'entrée est déclaré ─────────────────
        var endpoint = FirstNonEmpty(config, "healthCheckUrl", "baseUrl", "endpoint", "apiUrl", "url");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Rien à joindre : la configuration est valide, mais on ne peut pas
            // l'affirmer plus fort que ça. Ne pas prétendre à une connexion réussie.
            const string message = "Configuration valide. Aucun point d'entrée HTTP déclaré : connectivité non vérifiée.";
            instance.UpdateHealth(SyncHealth.Unknown, message);
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);

            return new ConnectionTestResult(true, 0, 0, ResolveCapabilities(definition), message);
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            var message = $"Point d'entrée invalide : « {endpoint} ».";
            instance.SetError(message);
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);

            return new ConnectionTestResult(false, 0, 0, [], message);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = httpClientFactory.CreateClient("ConnectorProbe");
            client.Timeout = ProbeTimeout;

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuthentication(request, definition.AuthType, config, credentials);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            stopwatch.Stop();

            var latency = (int)stopwatch.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                var message = $"Connexion établie ({(int)response.StatusCode} {response.ReasonPhrase}).";
                instance.UpdateHealth(latency > 3000 ? SyncHealth.Degraded : SyncHealth.Healthy, message);
                instance.Activate();
                instanceRepo.Update(instance);
                await instanceRepo.SaveAsync(ct);

                var items = TryReadItemCount(response);
                return new ConnectionTestResult(true, latency, items, ResolveCapabilities(definition), null);
            }

            // Un 401/403 distingue clairement un problème d'identifiants d'une panne réseau.
            var failure = response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                              or System.Net.HttpStatusCode.Forbidden
                ? $"Authentification refusée par le service distant ({(int)response.StatusCode})."
                : $"Le service distant a répondu {(int)response.StatusCode} {response.ReasonPhrase}.";

            instance.SetError(failure);
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);

            return new ConnectionTestResult(false, latency, 0, [], failure);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            var message = $"Délai dépassé après {ProbeTimeout.TotalSeconds:0} s.";
            instance.SetError(message);
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);

            return new ConnectionTestResult(false, (int)stopwatch.ElapsedMilliseconds, 0, [], message);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var message = $"Connexion impossible : {ex.Message}";
            instance.SetError(message);
            instanceRepo.Update(instance);
            await instanceRepo.SaveAsync(ct);

            logger.LogWarning(ex, "Test de connexion échoué pour l'instance {InstanceId}.", id);
            return new ConnectionTestResult(false, (int)stopwatch.ElapsedMilliseconds, 0, [], message);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SYNCHRONISATION
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<SyncRunResult> TriggerSyncAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");

        if (instance.Status != ConnectorInstanceStatus.Active)
            throw new InvalidOperationException(
                $"Le connecteur doit être actif pour lancer une synchronisation (état actuel : {instance.Status}).");

        instance.RecordSync();
        instanceRepo.Update(instance);

        // Replanifier les jobs cron rattachés à cette instance.
        var jobs = await syncJobRepo.GetByInstanceAsync(id, ct);
        foreach (var job in jobs.Where(j => j.Status == SyncJobStatus.Active))
        {
            job.RecordRun(SyncJobLastRunResult.Success, 0);
            ScheduleNext(job);
            syncJobRepo.Update(job);
        }

        await instanceRepo.SaveAsync(ct);
        await syncJobRepo.SaveAsync(ct);

        var executionId = Guid.CreateVersion7().ToString("N");
        return new SyncRunResult(executionId, $"/api/v1/connectors/instances/{id}/status/{executionId}");
    }

    public async Task<SyncJob> CreateSyncJobAsync(
        Guid tenantId, Guid instanceId, string name, SyncDirection direction,
        string? cronExpression, Guid actorId, CancellationToken ct = default)
    {
        _ = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new KeyNotFoundException("Instance de connecteur introuvable.");

        // Valider l'expression à la création plutôt que de découvrir l'erreur
        // au moment de la planification.
        if (!string.IsNullOrWhiteSpace(cronExpression) && !CronSchedule.TryParse(cronExpression, out _))
            throw new ArgumentException(
                $"Expression cron invalide : « {cronExpression} ». Format attendu : « minute heure jour mois jour-semaine ».");

        var job = SyncJob.Create(tenantId, instanceId, name, direction, actorId, cronExpression);
        ScheduleNext(job);

        await syncJobRepo.AddAsync(job, ct);
        await syncJobRepo.SaveAsync(ct);

        return job;
    }

    public async Task DeleteSyncJobAsync(Guid instanceId, Guid jobId, CancellationToken ct = default)
    {
        var job = await syncJobRepo.GetByIdAsync(jobId, ct);
        if (job == null || job.ConnectorInstanceId != instanceId)
            throw new KeyNotFoundException("Job de synchronisation introuvable pour cette instance.");

        syncJobRepo.SoftDelete(job);
        await syncJobRepo.SaveAsync(ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Calcule et pose la prochaine échéance d'un job planifié.</summary>
    private void ScheduleNext(SyncJob job)
    {
        if (string.IsNullOrWhiteSpace(job.CronExpression)) return;

        if (!CronSchedule.TryParse(job.CronExpression, out var schedule) || schedule is null)
        {
            logger.LogWarning("Expression cron illisible sur le job {JobId} : {Expression}", job.Id, job.CronExpression);
            return;
        }

        var next = schedule.GetNextOccurrence(DateTime.UtcNow);
        if (next.HasValue) job.ScheduleNextRun(next.Value);
    }

    /// <summary>
    /// Compare la configuration fournie au schéma déclaré par la définition
    /// (<c>SchemaJson</c>) et renvoie les champs obligatoires non renseignés.
    /// </summary>
    private static List<string> FindMissingRequiredFields(
        ConnectorDefinition definition,
        IReadOnlyDictionary<string, string>? config,
        IReadOnlyDictionary<string, string>? credentials)
    {
        var missing = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(definition.SchemaJson) ? "{}" : definition.SchemaJson);

            if (!doc.RootElement.TryGetProperty("required", out var required)
                || required.ValueKind != JsonValueKind.Array)
                return missing;

            foreach (var element in required.EnumerateArray())
            {
                var key = element.GetString();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var inConfig = config is not null
                    && config.TryGetValue(key, out var cv) && !string.IsNullOrWhiteSpace(cv);
                var inCreds = credentials is not null
                    && credentials.TryGetValue(key, out var kv) && !string.IsNullOrWhiteSpace(kv);

                if (!inConfig && !inCreds) missing.Add(key);
            }
        }
        catch (JsonException)
        {
            // Schéma illisible : on ne bloque pas la configuration sur cette base.
        }

        return missing;
    }

    /// <summary>Applique l'authentification attendue par le connecteur à la requête de sonde.</summary>
    private static void ApplyAuthentication(
        HttpRequestMessage request,
        ConnectorAuthType authType,
        IReadOnlyDictionary<string, string> config,
        IReadOnlyDictionary<string, string> credentials)
    {
        switch (authType)
        {
            case ConnectorAuthType.ApiKey:
            {
                var apiKey = FirstNonEmpty(credentials, "apiKey", "api_key", "token");
                if (string.IsNullOrWhiteSpace(apiKey)) return;

                // L'en-tête peut être imposé par le service distant (X-Api-Key, etc.).
                var header = FirstNonEmpty(config, "apiKeyHeader") ?? "X-Api-Key";
                request.Headers.TryAddWithoutValidation(header, apiKey);
                break;
            }

            case ConnectorAuthType.OAuth2:
            case ConnectorAuthType.Jwt:
            {
                var token = FirstNonEmpty(credentials, "accessToken", "access_token", "token", "jwt");
                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
            }

            case ConnectorAuthType.BasicAuth:
            {
                var user = FirstNonEmpty(credentials, "username", "user", "login");
                var pass = FirstNonEmpty(credentials, "password", "secret");
                if (string.IsNullOrWhiteSpace(user)) return;

                var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
                break;
            }

            case ConnectorAuthType.ServiceAccount:
            case ConnectorAuthType.SamlAssertion:
            {
                var assertion = FirstNonEmpty(credentials, "assertion", "serviceAccountKey", "token");
                if (!string.IsNullOrWhiteSpace(assertion))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion);
                break;
            }
        }
    }

    /// <summary>Certaines API annoncent leur volumétrie dans un en-tête de pagination.</summary>
    private static long TryReadItemCount(HttpResponseMessage response)
    {
        foreach (var header in new[] { "X-Total-Count", "X-Total", "X-Item-Count" })
        {
            if (response.Headers.TryGetValues(header, out var values)
                && long.TryParse(values.FirstOrDefault(), out var count))
                return count;
        }
        return 0;
    }

    private static string[] ResolveCapabilities(ConnectorDefinition definition) =>
        definition.SupportedCapabilities.Length > 0 ? definition.SupportedCapabilities : ["read"];

    private static Dictionary<string, string> DeserializeConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;

            // Tolérer une casse différente entre le schéma et la configuration saisie.
            var match = source.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value)) return match.Value;
        }
        return null;
    }
}
