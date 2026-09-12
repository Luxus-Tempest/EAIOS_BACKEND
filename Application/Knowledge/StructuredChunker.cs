using System.Text;
using System.Text.RegularExpressions;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Infrastructure.Extraction;

namespace EAIOS.Api.Application.Knowledge;

/// <summary>Un segment avant persistance : son texte, ses pages, son intitulé de section.</summary>
public sealed record ChunkDraft(string Content, int? StartPage, int? EndPage, string? Heading);

/// <summary>
/// Découpage respectant la structure du document.
///
/// L'ancien découpage coupait tous les mille caractères, sans égard pour les
/// pages ni les articles. Celui-ci suit les paragraphes, retient l'intitulé de
/// section courant (« Article 7.1 ») et la plage de pages de chaque segment :
/// c'est ce qui rend possible la citation « p. 3, art. 7.1 » du design.
/// </summary>
public static class StructuredChunker
{
    /// <summary>Taille visée d'un segment, en caractères (≈ 300 jetons).</summary>
    public const int TargetSize = 1200;

    /// <summary>Au-delà, un paragraphe est lui-même coupé.</summary>
    public const int MaxSize = 1800;

    /// <summary>Recouvrement : le dernier paragraphe (court) est repris en tête du segment suivant.</summary>
    public const int OverlapMax = 240;

    private static readonly Regex Heading = new(
        @"^\s*(?:(?:article|art\.?|chapitre|chapter|section|titre|title|annexe|annex|appendix|clause|partie|part)\s+[\divxlc]+(?:[.\-]\d+)*\b.*|\d+(?:\.\d+){0,3}\s*[.)\-–]?\s+[A-ZÀ-Ý][^\n]{2,80}|[A-ZÀ-Ý][A-ZÀ-Ý\s\d'’\-:]{6,80})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ParagraphBreak = new(@"\n\s*\n", RegexOptions.Compiled);

    /// <summary>Découpe un document paginé.</summary>
    public static List<ChunkDraft> Split(IReadOnlyList<ExtractedPage> pages)
    {
        var drafts = new List<ChunkDraft>();
        var buffer = new StringBuilder();
        int? bufferStart = null, bufferEnd = null;
        string? heading = null, bufferHeading = null;
        string? carry = null;

        void Flush()
        {
            var text = buffer.ToString().Trim();
            if (text.Length > 0)
                drafts.Add(new ChunkDraft(text, bufferStart, bufferEnd, bufferHeading));
            buffer.Clear();
            bufferStart = bufferEnd = null;
            bufferHeading = null;
        }

        void Append(string paragraph, int? page)
        {
            if (buffer.Length == 0)
            {
                bufferStart = page;
                bufferHeading = heading;
                if (carry is not null)
                {
                    buffer.Append(carry).Append("\n\n");
                    carry = null;
                }
            }
            else
            {
                buffer.Append("\n\n");
            }
            buffer.Append(paragraph);
            bufferEnd = page;
        }

        foreach (var page in pages)
        {
            var pageNumber = page.Number > 0 ? page.Number : (int?)null;

            foreach (var raw in ParagraphBreak.Split(page.Text))
            {
                var paragraph = raw.Trim();
                if (paragraph.Length == 0) continue;

                // Un intitulé de section clôt le segment courant et devient le
                // contexte des suivants.
                if (IsHeading(paragraph))
                {
                    if (buffer.Length > 0) { carry = null; Flush(); }
                    heading = Regex.Replace(paragraph, @"\s+", " ").Trim();
                    Append(paragraph, pageNumber);
                    continue;
                }

                foreach (var piece in Pieces(paragraph))
                {
                    if (buffer.Length > 0 && buffer.Length + piece.Length + 2 > TargetSize)
                    {
                        carry = LastParagraph(buffer.ToString());
                        Flush();
                    }
                    Append(piece, pageNumber);
                }
            }
        }

        Flush();
        return drafts;
    }

    /// <summary>Découpe un texte sans pagination (fiche saisie à la main).</summary>
    public static List<ChunkDraft> Split(string? content) =>
        string.IsNullOrWhiteSpace(content)
            ? []
            : Split([new ExtractedPage(0, content)]);

    /// <summary>Matérialise les segments d'un item.</summary>
    public static List<KnowledgeChunk> Materialize(Guid organizationId, Guid itemId, IEnumerable<ChunkDraft> drafts)
    {
        var chunks = new List<KnowledgeChunk>();
        var index = 0;
        foreach (var draft in drafts)
        {
            var chunk = KnowledgeChunk.Create(organizationId, itemId, index++, draft.Content, EstimateTokens(draft.Content));
            chunk.SetPageRange(draft.StartPage, draft.EndPage);
            chunk.SetContext(draft.Heading, null);
            chunks.Add(chunk);
        }
        return chunks;
    }

    public static int EstimateTokens(string text) => text.Length / 4 + 1;

    private static bool IsHeading(string paragraph) =>
        paragraph.Length <= 120 && !paragraph.Contains('\n') && Heading.IsMatch(paragraph);

    /// <summary>Un paragraphe trop long est coupé à la phrase.</summary>
    private static IEnumerable<string> Pieces(string paragraph)
    {
        if (paragraph.Length <= MaxSize)
        {
            yield return paragraph;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var sentence in Regex.Split(paragraph, @"(?<=[.!?;])\s+"))
        {
            if (current.Length > 0 && current.Length + sentence.Length + 1 > TargetSize)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }
        if (current.Length > 0) yield return current.ToString().Trim();
    }

    private static string? LastParagraph(string text)
    {
        var index = text.LastIndexOf("\n\n", StringComparison.Ordinal);
        var last = (index >= 0 ? text[(index + 2)..] : text).Trim();
        return last.Length is > 0 and <= OverlapMax ? last : null;
    }
}
