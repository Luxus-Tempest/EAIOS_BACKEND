using EAIOS.Api.Application.Common.Models;
using EAIOS.Api.Domain.Analytics;
using EAIOS.Api.Infrastructure.Persistence;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Misc;
using EAIOS.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EAIOS.Api.Application.Analytics;

// ── Contrat ───────────────────────────────────────────────────────────────────

public interface IReportService
{
    Task<ReportJob> RequestAsync(Guid tenantId, GenerateReportRequest request, Guid actorId, CancellationToken ct = default);
    Task<ReportJob> GetAsync(Guid reportId, Guid actorId, CancellationToken ct = default);
    Task<PagedResult<ReportJob>> ListAsync(Guid actorId, int page, int pageSize, CancellationToken ct = default);
    Task<ReportDownload> DownloadAsync(Guid reportId, Guid actorId, CancellationToken ct = default);

    /// <summary>Exécute la génération d'un job en attente. Appelé par le worker de fond.</summary>
    Task ProcessAsync(Guid reportId, CancellationToken ct = default);
}

public sealed record ReportDownload(Stream Content, string FileName, string ContentType, long SizeBytes);

public sealed record ReportJobDto(
    Guid Id,
    string ReportType,
    string Format,
    string Status,
    DateTime DateFrom,
    DateTime DateTo,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime ExpiresAt,
    string? FileName,
    long FileSizeBytes,
    int RowCount,
    string? FailureReason,
    string? DownloadUrl);

// ── Implémentation ────────────────────────────────────────────────────────────

/// <summary>
/// Génère de vrais fichiers de rapport (CSV ou JSON) à partir des données du tenant,
/// les dépose sur le stockage objet et suit l'avancement via l'entité <see cref="ReportJob"/>.
/// </summary>
public sealed class ReportService(
    IReportJobRepository jobRepo,
    EaiosDbContext db,
    PlatformDbContext platformDb,
    IAnalyticsQueryService analytics,
    IStorageService storage,
    ILogger<ReportService> logger,
    EAIOS.Api.Application.Notification.INotificationDispatcher? notifier = null,
    EAIOS.Api.Application.Realtime.IRealtimeEventService? realtime = null) : IReportService
{
    /// <summary>Garde-fou : au-delà, le rapport est tronqué et le fait est journalisé.</summary>
    private const int MaxRows = 100_000;

    private static readonly string[] SupportedTypes =
        ["dashboard", "agents", "workflows", "search", "documents", "users", "audit"];

    // ── Demande ───────────────────────────────────────────────────────────────

    public async Task<ReportJob> RequestAsync(Guid tenantId, GenerateReportRequest request, Guid actorId, CancellationToken ct = default)
    {
        var type = (request.ReportType ?? "").Trim().ToLowerInvariant();
        if (!SupportedTypes.Contains(type))
            throw new ArgumentException(
                $"Type de rapport inconnu : « {request.ReportType} ». Types supportés : {string.Join(", ", SupportedTypes)}.");

        var format = (request.Format ?? "csv").Trim().ToLowerInvariant();
        if (format != "csv" && format != "json")
            throw new ArgumentException($"Format non supporté : « {request.Format} ». Utilisez csv ou json.");

        var from = request.DateFrom == default ? DateTime.UtcNow.AddDays(-30) : request.DateFrom;
        var to   = request.DateTo   == default ? DateTime.UtcNow              : request.DateTo;
        if (to < from)
            throw new ArgumentException("DateTo doit être postérieure à DateFrom.");

        var parametersJson = request.Parameters is null ? "{}" : JsonSerializer.Serialize(request.Parameters);

        var job = ReportJob.Create(tenantId, type, format, from, to, actorId, parametersJson);
        await jobRepo.AddAsync(job, ct);
        await jobRepo.SaveAsync(ct);

        logger.LogInformation("Rapport {ReportId} demandé — type={Type} format={Format}", job.Id, type, format);
        return job;
    }

    public async Task<ReportJob> GetAsync(Guid reportId, Guid actorId, CancellationToken ct = default)
    {
        var job = await jobRepo.GetByIdAsync(reportId, ct)
            ?? throw new KeyNotFoundException("Rapport introuvable.");

        // Un rapport n'est visible que par son demandeur — le filtre tenant est déjà appliqué en amont.
        if (job.RequestedBy != actorId)
            throw new KeyNotFoundException("Rapport introuvable.");

        return job;
    }

    public Task<PagedResult<ReportJob>> ListAsync(Guid actorId, int page, int pageSize, CancellationToken ct = default) =>
        jobRepo.GetByRequesterAsync(actorId, page, pageSize, ct);

    public async Task<ReportDownload> DownloadAsync(Guid reportId, Guid actorId, CancellationToken ct = default)
    {
        var job = await GetAsync(reportId, actorId, ct);

        if (job.Status == ReportJobStatus.Failed)
            throw new InvalidOperationException($"La génération de ce rapport a échoué : {job.FailureReason}");

        if (job.Status is ReportJobStatus.Queued or ReportJobStatus.Running)
            throw new InvalidOperationException("Ce rapport est encore en cours de génération.");

        if (!job.IsDownloadable)
            throw new InvalidOperationException("Ce rapport a expiré et n'est plus téléchargeable.");

        var stream = await storage.OpenReadAsync(job.StorageKey!, ct)
            ?? throw new KeyNotFoundException("Le fichier de ce rapport est introuvable sur le stockage.");

        return new ReportDownload(stream, job.FileName ?? $"rapport-{job.Id:N}", job.ContentType ?? "application/octet-stream", job.FileSizeBytes);
    }

    // ── Génération ────────────────────────────────────────────────────────────

    public async Task ProcessAsync(Guid reportId, CancellationToken ct = default)
    {
        var job = await jobRepo.GetByIdAsync(reportId, ct);
        if (job is null || job.Status != ReportJobStatus.Queued)
            return;

        job.MarkRunning();
        jobRepo.Update(job);
        await jobRepo.SaveAsync(ct);

        try
        {
            var (rows, columns) = await BuildRowsAsync(job, ct);

            var truncated = rows.Count > MaxRows;
            if (truncated)
            {
                logger.LogWarning("Rapport {ReportId} tronqué à {Max} lignes (total {Total}).", job.Id, MaxRows, rows.Count);
                rows = rows.Take(MaxRows).ToList();
            }

            var (bytes, contentType, extension) = job.Format == "json"
                ? (SerializeJson(job, rows, truncated), "application/json; charset=utf-8", "json")
                : (SerializeCsv(columns, rows),         "text/csv; charset=utf-8",        "csv");

            var fileName = $"eaios-{job.ReportType}-{job.DateFrom:yyyyMMdd}-{job.DateTo:yyyyMMdd}.{extension}";

            using var ms = new MemoryStream(bytes);
            var uploaded = await storage.UploadAsync(
                ms, fileName, contentType, job.OrganizationId.ToString(), ct);

            job.MarkCompleted(uploaded.StorageKey, fileName, contentType, bytes.LongLength, rows.Count);
            jobRepo.Update(job);
            await jobRepo.SaveAsync(ct);

            logger.LogInformation("Rapport {ReportId} généré — {Rows} lignes, {Bytes} octets.", job.Id, rows.Count, bytes.LongLength);

            if (notifier is not null)
                await notifier.DispatchAsync(new EAIOS.Api.Application.Notification.NotificationRequest(
                    job.OrganizationId, job.RequestedBy, "report.completed",
                    "Rapport prêt", $"{fileName} — {rows.Count} ligne{(rows.Count > 1 ? "s" : "")}.",
                    "/reports", "Télécharger"), ct);
            if (realtime is not null)
                await realtime.PublishToUserAsync(job.OrganizationId, job.RequestedBy, "report.completed", new { reportId = job.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec de génération du rapport {ReportId}", job.Id);
            job.MarkFailed(ex.Message);
            jobRepo.Update(job);
            await jobRepo.SaveAsync(ct);
        }
    }

    /// <summary>Produit les lignes du rapport sous forme de dictionnaires ordonnés + l'ordre des colonnes.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, List<string> Columns)> BuildRowsAsync(
        ReportJob job, CancellationToken ct)
    {
        var from = job.DateFrom;
        var to   = job.DateTo;

        switch (job.ReportType)
        {
            case "documents":
            {
                var docs = await db.Documents
                    .Where(d => d.CreatedAt >= from && d.CreatedAt <= to)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => new
                    {
                        d.Id, d.Title, d.ResourceType, d.Classification, d.Status, d.IndexingStatus,
                        d.MimeType, d.FileSizeBytes, d.OwnerId, d.WorkspaceId, d.DepartmentId,
                        d.VersionCount, d.ViewCount, d.DownloadCount, d.CreatedAt, d.UpdatedAt
                    })
                    .ToListAsync(ct);

                var columns = new List<string> { "Id", "Titre", "Type", "Classification", "Statut", "Indexation",
                    "MimeType", "TailleOctets", "ProprietaireId", "WorkspaceId", "DepartementId",
                    "NbVersions", "Vues", "Telechargements", "CreeLe", "MajLe" };

                var rows = docs.Select(d => new Dictionary<string, object?>
                {
                    ["Id"]              = d.Id,
                    ["Titre"]           = d.Title,
                    ["Type"]            = d.ResourceType.ToString(),
                    ["Classification"]  = d.Classification.ToString(),
                    ["Statut"]          = d.Status.ToString(),
                    ["Indexation"]      = d.IndexingStatus.ToString(),
                    ["MimeType"]        = d.MimeType,
                    ["TailleOctets"]    = d.FileSizeBytes,
                    ["ProprietaireId"]  = d.OwnerId,
                    ["WorkspaceId"]     = d.WorkspaceId,
                    ["DepartementId"]   = d.DepartmentId,
                    ["NbVersions"]      = d.VersionCount,
                    ["Vues"]            = d.ViewCount,
                    ["Telechargements"] = d.DownloadCount,
                    ["CreeLe"]          = d.CreatedAt,
                    ["MajLe"]           = d.UpdatedAt
                }).ToList();

                return (rows, columns);
            }

            case "agents":
            {
                var executions = await db.AgentExecutions
                    .Where(e => e.StartedAt >= from && e.StartedAt <= to)
                    .OrderByDescending(e => e.StartedAt)
                    .Select(e => new
                    {
                        e.Id, e.AgentId, e.AgentVersion, e.UserId, e.Status, e.StartedAt, e.CompletedAt,
                        e.Duration, e.PromptTokens, e.CompletionTokens, e.TotalTokens, e.CostUsd,
                        e.ModelUsed, e.StepCount, e.ErrorCode, e.ErrorMessage
                    })
                    .ToListAsync(ct);

                var agentNames = (await db.Agents.Select(a => new { a.Id, a.Name }).ToListAsync(ct))
                    .ToDictionary(a => a.Id, a => a.Name);

                var columns = new List<string> { "ExecutionId", "AgentId", "Agent", "VersionAgent", "UtilisateurId",
                    "Statut", "DemarreLe", "TermineLe", "DureeMs", "TokensPrompt", "TokensCompletion",
                    "TokensTotal", "CoutUsd", "Modele", "NbEtapes", "CodeErreur", "MessageErreur" };

                var rows = executions.Select(e => new Dictionary<string, object?>
                {
                    ["ExecutionId"]      = e.Id,
                    ["AgentId"]          = e.AgentId,
                    ["Agent"]            = agentNames.GetValueOrDefault(e.AgentId) ?? "(supprimé)",
                    ["VersionAgent"]     = e.AgentVersion,
                    ["UtilisateurId"]    = e.UserId,
                    ["Statut"]           = e.Status.ToString(),
                    ["DemarreLe"]        = e.StartedAt,
                    ["TermineLe"]        = e.CompletedAt,
                    ["DureeMs"]          = e.Duration?.TotalMilliseconds,
                    ["TokensPrompt"]     = e.PromptTokens,
                    ["TokensCompletion"] = e.CompletionTokens,
                    ["TokensTotal"]      = e.TotalTokens,
                    ["CoutUsd"]          = e.CostUsd,
                    ["Modele"]           = e.ModelUsed,
                    ["NbEtapes"]         = e.StepCount,
                    ["CodeErreur"]       = e.ErrorCode,
                    ["MessageErreur"]    = e.ErrorMessage
                }).ToList();

                return (rows, columns);
            }

            case "workflows":
            {
                var instances = await db.WorkflowInstances
                    .Where(w => w.StartedAt >= from && w.StartedAt <= to)
                    .OrderByDescending(w => w.StartedAt)
                    .Select(w => new
                    {
                        w.Id, w.DefinitionId, w.DefinitionVersion, w.TriggerType, w.TriggeredBy,
                        w.Status, w.CurrentStepId, w.StartedAt, w.CompletedAt, w.DueAt, w.RetryCount, w.ErrorMessage
                    })
                    .ToListAsync(ct);

                var defNames = (await db.WorkflowDefinitions.Select(d => new { d.Id, d.Name }).ToListAsync(ct))
                    .ToDictionary(d => d.Id, d => d.Name);

                var columns = new List<string> { "InstanceId", "DefinitionId", "Workflow", "Version", "Declencheur",
                    "DeclencheParId", "Statut", "EtapeCourante", "DemarreLe", "TermineLe", "EcheanceLe",
                    "DureeHeures", "SlaRompu", "NbReprises", "MessageErreur" };

                var rows = instances.Select(w =>
                {
                    var duration = w.CompletedAt.HasValue ? (w.CompletedAt.Value - w.StartedAt).TotalHours : (double?)null;
                    var breached = w.DueAt.HasValue
                        && ((w.CompletedAt.HasValue && w.CompletedAt > w.DueAt)
                         || (!w.CompletedAt.HasValue && w.DueAt < DateTime.UtcNow));

                    return new Dictionary<string, object?>
                    {
                        ["InstanceId"]     = w.Id,
                        ["DefinitionId"]   = w.DefinitionId,
                        ["Workflow"]       = defNames.GetValueOrDefault(w.DefinitionId) ?? "(supprimé)",
                        ["Version"]        = w.DefinitionVersion,
                        ["Declencheur"]    = w.TriggerType.ToString(),
                        ["DeclencheParId"] = w.TriggeredBy,
                        ["Statut"]         = w.Status.ToString(),
                        ["EtapeCourante"]  = w.CurrentStepId,
                        ["DemarreLe"]      = w.StartedAt,
                        ["TermineLe"]      = w.CompletedAt,
                        ["EcheanceLe"]     = w.DueAt,
                        ["DureeHeures"]    = duration.HasValue ? Math.Round(duration.Value, 2) : null,
                        ["SlaRompu"]       = breached,
                        ["NbReprises"]     = w.RetryCount,
                        ["MessageErreur"]  = w.ErrorMessage
                    };
                }).ToList();

                return (rows, columns);
            }

            case "search":
            {
                var stats = await analytics.GetSearchAnalyticsAsync(job.OrganizationId, PeriodBetween(from, to), ct);

                var columns = new List<string> { "Requete", "Occurrences", "ScoreMoyen", "SansResultat" };

                var rows = stats.TopQueries
                    .Select(q => new Dictionary<string, object?>
                    {
                        ["Requete"]      = q.Query,
                        ["Occurrences"]  = q.Count,
                        ["ScoreMoyen"]   = Math.Round(q.AvgScore, 4),
                        ["SansResultat"] = false
                    })
                    .Concat(stats.ZeroResultQueries.Select(q => new Dictionary<string, object?>
                    {
                        ["Requete"]      = q.Query,
                        ["Occurrences"]  = q.Count,
                        ["ScoreMoyen"]   = 0f,
                        ["SansResultat"] = true
                    }))
                    .ToList();

                return (rows, columns);
            }

            case "users":
            {
                var users = await db.Users
                    .OrderBy(u => u.NormalizedEmail)
                    .Select(u => new
                    {
                        u.Id, u.Email, u.FirstName, u.LastName, u.JobTitle, u.Department,
                        u.Status, u.IsEmailVerified, u.IsMfaEnabled, u.Locale, u.TimeZone,
                        u.LastLoginAt, u.StorageUsedBytes, u.CreatedAt
                    })
                    .ToListAsync(ct);

                var columns = new List<string> { "Id", "Email", "Prenom", "Nom", "Poste", "Departement",
                    "Statut", "EmailVerifie", "MfaActive", "Langue", "FuseauHoraire",
                    "DerniereConnexion", "StockageOctets", "CreeLe" };

                var rows = users.Select(u => new Dictionary<string, object?>
                {
                    ["Id"]                = u.Id,
                    ["Email"]             = u.Email,
                    ["Prenom"]            = u.FirstName,
                    ["Nom"]               = u.LastName,
                    ["Poste"]             = u.JobTitle,
                    ["Departement"]       = u.Department,
                    ["Statut"]            = u.Status.ToString(),
                    ["EmailVerifie"]      = u.IsEmailVerified,
                    ["MfaActive"]         = u.IsMfaEnabled,
                    ["Langue"]            = u.Locale,
                    ["FuseauHoraire"]     = u.TimeZone,
                    ["DerniereConnexion"] = u.LastLoginAt,
                    ["StockageOctets"]    = u.StorageUsedBytes,
                    ["CreeLe"]            = u.CreatedAt
                }).ToList();

                return (rows, columns);
            }

            case "audit":
            {
                var events = await platformDb.AuditEvents
                    .AsNoTracking()
                    .Where(e => e.OrganizationId == job.OrganizationId
                             && e.OccurredAt >= from && e.OccurredAt <= to)
                    .OrderByDescending(e => e.OccurredAt)
                    .Take(MaxRows + 1)
                    .ToListAsync(ct);

                var columns = new List<string> { "Id", "SurvenuLe", "ActeurId", "TypeActeur", "EmailActeur",
                    "IpActeur", "Action", "Module", "Resultat", "RaisonEchec",
                    "RessourceId", "TypeRessource", "NomRessource", "CorrelationId" };

                var rows = events.Select(e => new Dictionary<string, object?>
                {
                    ["Id"]            = e.Id,
                    ["SurvenuLe"]     = e.OccurredAt,
                    ["ActeurId"]      = e.ActorId,
                    ["TypeActeur"]    = e.ActorType,
                    ["EmailActeur"]   = e.ActorEmail,
                    ["IpActeur"]      = e.ActorIp,
                    ["Action"]        = e.Action,
                    ["Module"]        = e.Module,
                    ["Resultat"]      = e.Result.ToString(),
                    ["RaisonEchec"]   = e.FailureReason,
                    ["RessourceId"]   = e.ResourceId,
                    ["TypeRessource"] = e.ResourceType,
                    ["NomRessource"]  = e.ResourceName,
                    ["CorrelationId"] = e.CorrelationId
                }).ToList();

                return (rows, columns);
            }

            case "dashboard":
            default:
            {
                var period = PeriodBetween(from, to);
                var dash   = await analytics.GetDashboardAsync(job.OrganizationId, period, ct);

                var columns = new List<string> { "Date", "UtilisateursActifs", "Documents", "Recherches", "ExecutionsAgents" };

                var rows = dash.UsageByDate.Select(u => new Dictionary<string, object?>
                {
                    ["Date"]               = u.Date,
                    ["UtilisateursActifs"] = u.ActiveUsers,
                    ["Documents"]          = u.Documents,
                    ["Recherches"]         = u.Searches,
                    ["ExecutionsAgents"]   = u.AgentExecutions
                }).ToList();

                return (rows, columns);
            }
        }
    }

    // ── Sérialisation ─────────────────────────────────────────────────────────

    /// <summary>CSV RFC 4180 avec BOM UTF-8 pour qu'Excel ouvre correctement les accents.</summary>
    private static byte[] SerializeCsv(List<string> columns, List<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', columns.Select(EscapeCsv)));

        foreach (var row in rows)
            sb.AppendLine(string.Join(',', columns.Select(c => EscapeCsv(FormatValue(row.GetValueOrDefault(c))))));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static byte[] SerializeJson(ReportJob job, List<Dictionary<string, object?>> rows, bool truncated)
    {
        var payload = new
        {
            report = new
            {
                id       = job.Id,
                type     = job.ReportType,
                dateFrom = job.DateFrom,
                dateTo   = job.DateTo,
                rowCount = rows.Count,
                truncated,
                generatedAt = DateTime.UtcNow
            },
            data = rows
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        });
    }

    private static string FormatValue(object? value) => value switch
    {
        null              => "",
        bool b            => b ? "true" : "false",
        DateTime dt       => dt.ToString("o", CultureInfo.InvariantCulture),
        decimal d         => d.ToString(CultureInfo.InvariantCulture),
        double db         => db.ToString(CultureInfo.InvariantCulture),
        float f           => f.ToString(CultureInfo.InvariantCulture),
        IFormattable form => form.ToString(null, CultureInfo.InvariantCulture),
        _                 => value.ToString() ?? ""
    };

    private static string EscapeCsv(string? field)
    {
        field ??= "";
        // Neutralise l'injection de formules dans les tableurs.
        if (field.Length > 0 && (field[0] is '=' or '+' or '-' or '@'))
            field = "'" + field;

        return field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }

    /// <summary>Traduit une fenêtre absolue en période relative comprise par IAnalyticsQueryService.</summary>
    private static string PeriodBetween(DateTime from, DateTime to)
    {
        var days = Math.Max(1, (int)Math.Ceiling((to - from).TotalDays));
        return $"{days}d";
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    public static ReportJobDto Map(ReportJob j) => new(
        Id:            j.Id,
        ReportType:    j.ReportType,
        Format:        j.Format,
        Status:        j.Status.ToString(),
        DateFrom:      j.DateFrom,
        DateTo:        j.DateTo,
        CreatedAt:     j.CreatedAt,
        StartedAt:     j.StartedAt,
        CompletedAt:   j.CompletedAt,
        ExpiresAt:     j.ExpiresAt,
        FileName:      j.FileName,
        FileSizeBytes: j.FileSizeBytes,
        RowCount:      j.RowCount,
        FailureReason: j.FailureReason,
        DownloadUrl:   j.IsDownloadable ? $"/api/v1/analytics/reports/{j.Id}/download" : null);
}
