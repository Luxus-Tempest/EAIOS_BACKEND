namespace EAIOS.Api.Application.Connector;

/// <summary>
/// Évaluateur d'expressions cron à 5 champs (minute heure jour-du-mois mois jour-de-semaine),
/// utilisé pour calculer <c>SyncJob.NextRunAt</c>. Sans lui, les jobs planifiés
/// n'auraient jamais de prochaine échéance et ne seraient jamais repris par
/// <c>ISyncJobRepository.GetDueAsync</c>.
///
/// Syntaxe supportée par champ : <c>*</c>, valeur, liste <c>a,b</c>, plage <c>a-b</c>,
/// pas <c>*&#47;n</c> et <c>a-b/n</c>. Les raccourcis usuels (<c>@hourly</c>,
/// <c>@daily</c>, <c>@weekly</c>, <c>@monthly</c>) sont également acceptés.
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] _minutes    = new bool[60];
    private readonly bool[] _hours      = new bool[24];
    private readonly bool[] _daysOfMonth = new bool[32];  // index 1..31
    private readonly bool[] _months     = new bool[13];   // index 1..12
    private readonly bool[] _daysOfWeek = new bool[7];    // 0 = dimanche

    /// <summary>Vrai si le champ jour-du-mois et le champ jour-de-semaine sont tous deux restreints.</summary>
    private readonly bool _domRestricted;
    private readonly bool _dowRestricted;

    public string Expression { get; }

    private CronSchedule(string expression, bool domRestricted, bool dowRestricted)
    {
        Expression     = expression;
        _domRestricted = domRestricted;
        _dowRestricted = dowRestricted;
    }

    /// <summary>Analyse une expression cron. Lève <see cref="FormatException"/> si elle est invalide.</summary>
    public static CronSchedule Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FormatException("L'expression cron est vide.");

        var expr = expression.Trim();

        expr = expr.ToLowerInvariant() switch
        {
            "@hourly"  => "0 * * * *",
            "@daily" or "@midnight" => "0 0 * * *",
            "@weekly"  => "0 0 * * 0",
            "@monthly" => "0 0 1 * *",
            "@yearly" or "@annually" => "0 0 1 1 *",
            _ => expr
        };

        var fields = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            throw new FormatException(
                $"L'expression cron doit comporter 5 champs (minute heure jour mois jour-semaine), reçu {fields.Length}.");

        var domRestricted = fields[2] != "*";
        var dowRestricted = fields[4] != "*";

        var schedule = new CronSchedule(expression.Trim(), domRestricted, dowRestricted);

        Fill(schedule._minutes,     fields[0], 0,  59, "minute");
        Fill(schedule._hours,       fields[1], 0,  23, "heure");
        Fill(schedule._daysOfMonth, fields[2], 1,  31, "jour du mois");
        Fill(schedule._months,      fields[3], 1,  12, "mois");
        Fill(schedule._daysOfWeek,  fields[4], 0,  6,  "jour de la semaine");

        return schedule;
    }

    public static bool TryParse(string? expression, out CronSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        try
        {
            schedule = Parse(expression);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Prochaine occurrence strictement postérieure à <paramref name="after"/> (UTC),
    /// ou <c>null</c> si aucune n'existe dans les 5 prochaines années.
    /// </summary>
    public DateTime? GetNextOccurrence(DateTime after)
    {
        // Repartir de la minute suivante : une occurrence doit être strictement future.
        var cursor = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);

        var limit = cursor.AddYears(5);

        while (cursor < limit)
        {
            if (!_months[cursor.Month])
            {
                // Sauter directement au 1er du mois suivant.
                cursor = new DateTime(cursor.Year, cursor.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                continue;
            }

            if (!MatchesDay(cursor))
            {
                cursor = cursor.Date.AddDays(1);
                continue;
            }

            if (!_hours[cursor.Hour])
            {
                cursor = cursor.Date.AddHours(cursor.Hour + 1);
                continue;
            }

            if (!_minutes[cursor.Minute])
            {
                cursor = cursor.AddMinutes(1);
                continue;
            }

            return cursor;
        }

        return null;
    }

    /// <summary>
    /// Convention cron standard : si les deux champs de jour sont restreints,
    /// l'occurrence vaut dès que l'un OU l'autre correspond.
    /// </summary>
    private bool MatchesDay(DateTime moment)
    {
        var domMatch = _daysOfMonth[moment.Day];
        var dowMatch = _daysOfWeek[(int)moment.DayOfWeek];

        if (_domRestricted && _dowRestricted) return domMatch || dowMatch;
        if (_domRestricted)                   return domMatch;
        if (_dowRestricted)                   return dowMatch;
        return true;
    }

    private static void Fill(bool[] target, string field, int min, int max, string fieldName)
    {
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var step  = 1;
            var range = part;

            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                range = part[..slash];
                if (!int.TryParse(part[(slash + 1)..], out step) || step <= 0)
                    throw new FormatException($"Pas invalide dans le champ {fieldName} : « {part} ».");
            }

            int from, to;

            if (range is "*" or "")
            {
                from = min;
                to   = max;
            }
            else if (range.Contains('-'))
            {
                var bounds = range.Split('-', 2);
                if (!int.TryParse(bounds[0], out from) || !int.TryParse(bounds[1], out to))
                    throw new FormatException($"Plage invalide dans le champ {fieldName} : « {range} ».");
            }
            else
            {
                if (!int.TryParse(range, out from))
                    throw new FormatException($"Valeur invalide dans le champ {fieldName} : « {range} ».");
                to = from;
            }

            // Cron accepte 7 comme dimanche : on le ramène sur 0.
            if (max == 6)
            {
                if (from == 7) from = 0;
                if (to == 7)   to = 0;
            }

            if (from < min || to > max || from > to)
                throw new FormatException(
                    $"Le champ {fieldName} doit rester entre {min} et {max} (reçu « {range} »).");

            for (var value = from; value <= to; value += step)
                target[value] = true;
        }
    }
}
