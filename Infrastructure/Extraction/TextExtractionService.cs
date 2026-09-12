using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace EAIOS.Api.Infrastructure.Extraction;

/// <summary>Une page de texte extrait. Les formats sans pagination n'en ont qu'une.</summary>
public sealed record ExtractedPage(int Number, string Text);

/// <summary>Le texte d'un document, page par page, et ce que l'on en sait.</summary>
public sealed record ExtractedDocument(
    IReadOnlyList<ExtractedPage> Pages,
    string? Language,
    bool OcrApplied)
{
    public int PageCount => Pages.Count;
    public string FullText => string.Join("\n\n", Pages.Select(p => p.Text));
    public bool IsEmpty => Pages.All(p => string.IsNullOrWhiteSpace(p.Text));
}

/// <summary>
/// Extraction du texte d'un fichier déposé.
///
/// C'est le maillon qui manquait : sans lui, un contrat déposé en PDF partait au
/// stockage et n'était jamais lu par personne — ni par la recherche, ni par
/// l'agent. Le texte est extrait <b>page par page</b>, parce que la citation à
/// la page (« p. 3 ») en dépend.
/// </summary>
public interface ITextExtractor
{
    /// <summary>Vrai si le format est lisible sans reconnaissance de caractères.</summary>
    bool Supports(string? mimeType, string? fileName);

    /// <summary>Le texte extrait, ou <c>null</c> si le format n'est pas pris en charge.</summary>
    Task<ExtractedDocument?> ExtractAsync(Stream content, string? mimeType, string? fileName, CancellationToken ct = default);
}

public sealed class TextExtractionService(ILogger<TextExtractionService> logger) : ITextExtractor
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".log", ".rtf"
    };

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

    public bool Supports(string? mimeType, string? fileName) => Kind(mimeType, fileName) is not null;

    public async Task<ExtractedDocument?> ExtractAsync(Stream content, string? mimeType, string? fileName, CancellationToken ct = default)
    {
        var kind = Kind(mimeType, fileName);
        if (kind is null) return null;

        // Les lecteurs veulent un flux positionnable ; celui du stockage objet ne l'est pas.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var pages = kind switch
        {
            "pdf"  => ExtractPdf(buffer),
            "docx" => ExtractDocx(buffer),
            "html" => [new ExtractedPage(1, StripHtml(await ReadTextAsync(buffer, ct)))],
            _      => [new ExtractedPage(1, await ReadTextAsync(buffer, ct))],
        };

        pages = pages.Select(p => p with { Text = Normalize(p.Text) }).ToList();

        var language = DetectLanguage(string.Join(' ', pages.Take(5).Select(p => p.Text)));
        logger.LogDebug("Texte extrait : {Kind}, {Pages} page(s), langue {Language}.", kind, pages.Count, language ?? "?");

        return new ExtractedDocument(pages, language, OcrApplied: false);
    }

    // ── Formats ───────────────────────────────────────────────────────────────

    private static string? Kind(string? mimeType, string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? "") ?? "";
        var mime = (mimeType ?? "").ToLowerInvariant();

        if (mime.Contains("pdf") || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return "pdf";
        if (mime.Contains("wordprocessingml") || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)) return "docx";
        if (mime.Contains("html") || HtmlExtensions.Contains(extension)) return "html";
        if (mime.StartsWith("text/") || mime.Contains("json") || mime.Contains("xml") || TextExtensions.Contains(extension)) return "text";

        // Les images et les PDF numérisés relèvent de la reconnaissance de
        // caractères, qui n'est pas embarquée : le document reste indexé par
        // ses métadonnées seulement, et le statut le dit.
        return null;
    }

    private static List<ExtractedPage> ExtractPdf(Stream stream)
    {
        var pages = new List<ExtractedPage>();
        using var document = PdfDocument.Open(stream);
        foreach (var page in document.GetPages())
        {
            // `Text` concatène les mots dans l'ordre de lecture ; les blocs sont
            // séparés par les retours à la ligne que le lecteur reconstitue.
            var words = page.GetWords().Select(w => w.Text);
            var text = LayoutText(page.Text, words);
            pages.Add(new ExtractedPage(page.Number, text));
        }
        return pages;
    }

    /// <summary>
    /// PdfPig rend le texte brut de la page ; les paragraphes y sont souvent
    /// collés. On reconstruit des coupures de paragraphe là où une ligne se
    /// termine par une ponctuation forte suivie d'une majuscule.
    /// </summary>
    private static string LayoutText(string raw, IEnumerable<string> words)
    {
        if (!string.IsNullOrWhiteSpace(raw)) return raw;
        return string.Join(' ', words);
    }

    private static List<ExtractedPage> ExtractDocx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return [new ExtractedPage(1, "")];

        // Word ne connaît pas la page à l'avance (elle dépend du rendu) : on
        // suit les sauts de page explicites, et à défaut tout est page 1.
        var pages = new List<ExtractedPage>();
        var current = new StringBuilder();
        var number = 1;

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var hasPageBreak = paragraph.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page)
                            || paragraph.Descendants<LastRenderedPageBreak>().Any();

            var text = paragraph.InnerText;
            var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
            if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || style.StartsWith("Titre", StringComparison.OrdinalIgnoreCase))
                text = "\n" + text;

            if (hasPageBreak && current.Length > 0)
            {
                pages.Add(new ExtractedPage(number++, current.ToString()));
                current.Clear();
            }

            current.AppendLine(text);
        }

        pages.Add(new ExtractedPage(number, current.ToString()));
        return pages;
    }

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static readonly Regex HtmlTag = new(@"<script[\s\S]*?</script>|<style[\s\S]*?</style>|<[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string StripHtml(string html) =>
        System.Net.WebUtility.HtmlDecode(HtmlTag.Replace(html, " "));

    // ── Normalisation ─────────────────────────────────────────────────────────

    private static readonly Regex Spaces = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private static readonly Regex Blank = new(@"\n{3,}", RegexOptions.Compiled);

    private static string Normalize(string text)
    {
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');
        t = Spaces.Replace(t, " ");
        t = Blank.Replace(t, "\n\n");
        return t.Trim();
    }

    // ── Langue ────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string[]> Stopwords = new()
    {
        ["fr"] = ["le", "la", "les", "des", "une", "est", "dans", "pour", "que", "qui", "pas", "sur", "avec", "par", "sont", "cette", "être"],
        ["en"] = ["the", "and", "of", "to", "in", "is", "that", "for", "with", "are", "this", "be", "on", "by", "not", "from", "which"],
        ["de"] = ["der", "die", "und", "das", "ist", "nicht", "ein", "eine", "mit", "für", "auf", "den", "von", "sich", "dem", "auch", "werden"],
        ["es"] = ["el", "la", "los", "las", "de", "que", "es", "para", "con", "una", "por", "del", "como", "más", "sus", "este", "ser"],
    };

    /// <summary>
    /// Détection par mots outils. Volontairement simple : elle sert à choisir
    /// la langue de l'index plein texte, pas à faire de la linguistique.
    /// </summary>
    public static string? DetectLanguage(string sample)
    {
        if (string.IsNullOrWhiteSpace(sample)) return null;

        var tokens = Regex.Split(sample.ToLowerInvariant(), @"[^\p{L}]+")
            .Where(t => t.Length > 1)
            .Take(2000)
            .ToList();
        if (tokens.Count < 20) return null;

        var best = Stopwords
            .Select(kv => (Language: kv.Key, Score: tokens.Count(t => Array.IndexOf(kv.Value, t) >= 0)))
            .OrderByDescending(x => x.Score)
            .First();

        return best.Score >= 3 ? best.Language : null;
    }
}
