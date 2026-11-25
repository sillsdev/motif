using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIL.Motif.Contract.Gdr;

/// <summary>The line labels of an interlinear example, in the order they are written.</summary>
public static class GuideInterlinearLabels
{
    /// <summary>The example word or phrase, in the vernacular writing system.</summary>
    public const string Word = "word";

    /// <summary>The same text cut into morphemes, in the vernacular writing system.</summary>
    public const string Morphemes = "morphemes";

    /// <summary>One gloss for each morpheme, in the analysis writing system.</summary>
    public const string Gloss = "gloss";

    /// <summary>The free translation, in the analysis writing system; the one optional line.</summary>
    public const string Translation = "translation";
}

/// <summary>One word of an interlinear example: its surface form, its morphemes and their glosses.</summary>
/// <param name="Word">The surface token; empty when the line has fewer tokens than another.</param>
/// <param name="Morphemes">The morpheme token, with its hyphens and equals signs.</param>
/// <param name="Gloss">The gloss token, with its hyphens and equals signs.</param>
public sealed record GuideInterlinearColumn(string Word, string Morphemes, string Gloss)
{
    /// <summary>The morphemes alone, without the hyphens and equals signs that join or mark them.</summary>
    public IReadOnlyList<string> MorphemePieces => GuideInterlinear.Pieces(Morphemes);

    /// <summary>The glosses alone, one for each morpheme.</summary>
    public IReadOnlyList<string> GlossPieces => GuideInterlinear.Pieces(Gloss);
}

/// <summary>One interlinear example.</summary>
/// <param name="Word">The text of the word line.</param>
/// <param name="Morphemes">The text of the morphemes line.</param>
/// <param name="Gloss">The text of the gloss line.</param>
/// <param name="Translation">The text of the translation line, or null when the example has none.</param>
/// <param name="Columns">The tokens of the three lines side by side, as many columns as the longest line has tokens.</param>
/// <param name="Span">Where the block sits; its lines are paragraphs when it was read from stored paragraphs.</param>
public sealed record GuideInterlinearBlock(string Word, string Morphemes, string Gloss, string? Translation,
    IReadOnlyList<GuideInterlinearColumn> Columns, GuideBlockSpan Span)
{
    /// <summary>Whether the three lines have the same number of tokens.</summary>
    public bool IsAligned => Columns.All(column => column.Word.Length > 0 && column.Morphemes.Length > 0 && column.Gloss.Length > 0);

    /// <summary>The block as a monospace grid, one row for each line, columns padded to the widest cell, then the translation.</summary>
    public string ToGrid()
    {
        var widths = Columns.Select(column => new[] { column.Word, column.Morphemes, column.Gloss }.Max(Width)).ToArray();
        string Row(Func<GuideInterlinearColumn, string> pick) =>
            string.Join("  ", Columns.Select((column, index) => pick(column) + new string(' ', widths[index] - Width(pick(column))))).TrimEnd();
        var rows = new List<string> { Row(column => column.Word), Row(column => column.Morphemes), Row(column => column.Gloss) };
        if (!string.IsNullOrEmpty(Translation)) rows.Add(Translation);
        return string.Join("\n", rows);
    }

    private static int Width(string text) => new StringInfo(text).LengthInTextElements;
}

/// <summary>The kinds of problem an interlinear block can have.</summary>
public enum GuideInterlinearDiagnosticKind
{
    /// <summary>The block's closing fence is missing.</summary>
    UnclosedBlock,
    /// <summary>A line is not <c>label: text</c>.</summary>
    MalformedLine,
    /// <summary>A label other than word, morphemes, gloss and translation.</summary>
    UnknownLabel,
    /// <summary>A label appears twice in one block; the first is kept.</summary>
    DuplicateLabel,
    /// <summary>The word, morphemes or gloss line is absent.</summary>
    MissingLine,
    /// <summary>The word, morphemes and gloss lines do not have the same number of tokens.</summary>
    TokenCountMismatch,
    /// <summary>A token's morphemes and its glosses are not the same number.</summary>
    GlossMismatch,
}

/// <summary>One problem found in an interlinear block.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Message">A sentence a person reads.</param>
/// <param name="Span">The block it is in.</param>
/// <param name="Line">The 1-based line of the problem, or the block's first line.</param>
public sealed record GuideInterlinearDiagnostic(GuideInterlinearDiagnosticKind Kind, string Message, GuideBlockSpan Span, int Line);

/// <summary>Everything the parser found, in document order.</summary>
public sealed record GuideInterlinearBlocks(IReadOnlyList<GuideInterlinearBlock> Blocks,
    IReadOnlyList<GuideInterlinearDiagnostic> Diagnostics);

/// <summary>
/// Finds the interlinear example blocks of a Guide section and never throws on bad input. A block is a fenced div,
/// <c>::: {.interlinear}</c> to <c>:::</c>, holding the lines <c>word:</c>, <c>morphemes:</c>, <c>gloss:</c> and optionally
/// <c>translation:</c>; stored in the project it is those lines as plain paragraphs, which <see cref="Expand"/> and
/// <see cref="Fold"/> convert between.
/// </summary>
public static partial class GuideInterlinear
{
    /// <summary>The opening line of the div.</summary>
    public const string Opening = "::: {.interlinear}";

    private static readonly string[] Order =
        [GuideInterlinearLabels.Word, GuideInterlinearLabels.Morphemes, GuideInterlinearLabels.Gloss, GuideInterlinearLabels.Translation];

    [GeneratedRegex(@"^:{3,}\s*\{\s*\.interlinear\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OpeningLine();

    [GeneratedRegex(@"^:{3,}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ClosingLine();

    [GeneratedRegex(@"^\s*([A-Za-z]+)\s*:\s?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelledLine();

    [GeneratedRegex(@"\[([^\[\]]*)\]\{[^{}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex SpanMark();

    [GeneratedRegex(@"\\([\p{P}\p{S}])", RegexOptions.CultureInvariant)]
    private static partial Regex Escape();

    /// <summary>Parses the fenced divs of <paramref name="markdown"/>; a block with a problem is still returned where it can be placed.</summary>
    public static GuideInterlinearBlocks Parse(string? markdown)
    {
        markdown ??= "";
        var blocks = new List<GuideInterlinearBlock>();
        var diagnostics = new List<GuideInterlinearDiagnostic>();
        var lines = Lines(markdown);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!OpeningLine().IsMatch(lines[i].Text)) continue;
            var close = -1;
            for (var j = i + 1; j < lines.Count; j++)
                if (ClosingLine().IsMatch(lines[j].Text)) { close = j; break; }
            var last = close < 0 ? lines.Count - 1 : close;
            var body = lines.Skip(i + 1).Take((close < 0 ? lines.Count : close) - i - 1).ToList();
            var span = new GuideBlockSpan(lines[i].Start, lines[last].End, i + 1, last + 1,
                body.Count > 0 ? body[0].Start : lines[i].End, body.Count > 0 ? body[^1].End : lines[i].End);
            if (close < 0)
                diagnostics.Add(new(GuideInterlinearDiagnosticKind.UnclosedBlock, "The block has no closing fence.", span, i + 1));
            Read(body.Select(line => (line.Text, line.Number)).ToList(), span, blocks, diagnostics);
            if (close < 0) break;
            i = close;
        }
        return new(blocks, diagnostics);
    }

    /// <summary>
    /// Parses stored paragraphs, each the text of one: a run of paragraphs labelled word, morphemes and gloss, then
    /// optionally translation, is one block. Text that does not start such a run is ordinary prose.
    /// </summary>
    public static GuideInterlinearBlocks ParseStored(IReadOnlyList<string> paragraphs)
    {
        paragraphs ??= [];
        var blocks = new List<GuideInterlinearBlock>();
        var diagnostics = new List<GuideInterlinearDiagnostic>();
        var offsets = new int[paragraphs.Count + 1];
        for (var i = 0; i < paragraphs.Count; i++) offsets[i + 1] = offsets[i] + (paragraphs[i] ?? "").Length + 1;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (LabelOf(paragraphs[i]) != GuideInterlinearLabels.Word) continue;
            var count = 1;
            while (count < 4 && i + count < paragraphs.Count && LabelOf(paragraphs[i + count]) == Order[count]) count++;
            var end = i + count - 1;
            var span = new GuideBlockSpan(offsets[i], offsets[end + 1] - 1, i + 1, end + 1, offsets[i], offsets[end + 1] - 1);
            var run = Enumerable.Range(i, count).Select(index => (paragraphs[index] ?? "", index + 1)).ToList();
            Read(run, span, blocks, diagnostics);
            i = end;
        }
        return new(blocks, diagnostics);
    }

    /// <summary>
    /// Rewrites each well-formed interlinear div as its lines, one paragraph each, so FieldWorks Markdown reads it as plain
    /// text. A div that is not well formed, or not closed, is left as it was.
    /// </summary>
    public static string Expand(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (!markdown.Contains("interlinear", StringComparison.Ordinal)) return markdown;
        var lines = markdown.Split('\n');
        var result = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!OpeningLine().IsMatch(lines[i].TrimEnd('\r'))) { result.Add(lines[i]); continue; }
            var close = -1;
            for (var j = i + 1; j < lines.Length; j++)
                if (ClosingLine().IsMatch(lines[j].TrimEnd('\r'))) { close = j; break; }
            var body = close < 0 ? [] : lines.Skip(i + 1).Take(close - i - 1).Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0).ToArray();
            if (close < 0 || !WellFormed(body)) { result.Add(lines[i]); continue; }
            if (result.Count > 0 && result[^1].Trim().Length > 0) result.Add("");
            for (var k = 0; k < body.Length; k++)
            {
                result.Add(body[k].Trim());
                if (k + 1 < body.Length) result.Add("");
            }
            if (close + 1 < lines.Length && lines[close + 1].Trim().Length > 0) result.Add("");
            i = close;
        }
        return string.Join("\n", result);
    }

    /// <summary>
    /// The inverse of <see cref="Expand"/>: each run of four (or three) single-line paragraphs labelled word, morphemes,
    /// gloss and translation becomes one interlinear div. Runs inside a div are left alone.
    /// </summary>
    public static string Fold(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (!markdown.Contains("word:", StringComparison.OrdinalIgnoreCase)) return markdown;
        var lines = markdown.Split('\n');
        var result = new List<string>();
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (ClosingLine().IsMatch(line.TrimEnd('\r'))) depth = Math.Max(0, depth - 1);
            else if (line.StartsWith(":::", StringComparison.Ordinal)) depth++;
            if (depth == 0 && LabelOf(line) == GuideInterlinearLabels.Word && (i == 0 || lines[i - 1].Trim().Length == 0) &&
                (i + 1 == lines.Length || lines[i + 1].Trim().Length == 0))
            {
                var taken = new List<string> { line.Trim() };
                var next = i;
                while (taken.Count < 4 && next + 2 < lines.Length && lines[next + 1].Trim().Length == 0 &&
                    LabelOf(lines[next + 2]) == Order[taken.Count] && (next + 3 == lines.Length || lines[next + 3].Trim().Length == 0))
                {
                    taken.Add(lines[next + 2].Trim());
                    next += 2;
                }
                if (taken.Count >= 3)
                {
                    result.Add(Opening);
                    result.AddRange(taken);
                    result.Add(":::");
                    i = next;
                    continue;
                }
            }
            result.Add(line);
        }
        return string.Join("\n", result);
    }

    /// <summary>The tokens of a line: its text cut at white space, with the span and escape marks of FieldWorks Markdown removed.</summary>
    public static IReadOnlyList<string> Tokens(string? text) =>
        Clean(text).Split([' ', '\t', ' ', '​'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The morphemes or glosses of one token: its text cut at hyphens and equals signs, leaving out empty pieces.</summary>
    public static IReadOnlyList<string> Pieces(string? token) =>
        (token ?? "").Split(['-', '=', '~'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Clean(string? text) => Escape().Replace(SpanMark().Replace(text ?? "", "$1"), "$1").Trim();

    private static string? LabelOf(string? text)
    {
        var match = LabelledLine().Match((text ?? "").TrimEnd('\r'));
        if (!match.Success) return null;
        var label = match.Groups[1].Value.ToLowerInvariant();
        return Array.IndexOf(Order, label) >= 0 ? label : null;
    }

    private static bool WellFormed(string[] body)
    {
        if (body.Length is not (3 or 4)) return false;
        for (var i = 0; i < body.Length; i++)
            if (LabelOf(body[i]) != Order[i]) return false;
        return true;
    }

    private static void Read(List<(string Text, int Line)> body, GuideBlockSpan span, List<GuideInterlinearBlock> blocks,
        List<GuideInterlinearDiagnostic> diagnostics)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (text, number) in body)
        {
            if (text.Trim().Length == 0) continue;
            var match = LabelledLine().Match(text);
            if (!match.Success)
            {
                diagnostics.Add(new(GuideInterlinearDiagnosticKind.MalformedLine, "A line is label: text.", span, number));
                continue;
            }
            var label = match.Groups[1].Value.ToLowerInvariant();
            if (Array.IndexOf(Order, label) < 0)
                diagnostics.Add(new(GuideInterlinearDiagnosticKind.UnknownLabel,
                    $"The label \"{match.Groups[1].Value}\" is not word, morphemes, gloss or translation.", span, number));
            else if (!found.TryAdd(label, match.Groups[2].Value.Trim()))
                diagnostics.Add(new(GuideInterlinearDiagnosticKind.DuplicateLabel, $"The label \"{label}\" appears twice.", span, number));
        }
        var missing = Order.Take(3).Where(label => !found.ContainsKey(label)).ToArray();
        foreach (var label in missing)
            diagnostics.Add(new(GuideInterlinearDiagnosticKind.MissingLine, $"An example needs a {label} line.", span, span.StartLine));
        if (missing.Length > 0) return;
        var words = Tokens(found[GuideInterlinearLabels.Word]);
        var morphemes = Tokens(found[GuideInterlinearLabels.Morphemes]);
        var glosses = Tokens(found[GuideInterlinearLabels.Gloss]);
        var width = Math.Max(words.Count, Math.Max(morphemes.Count, glosses.Count));
        string At(IReadOnlyList<string> tokens, int index) => index < tokens.Count ? tokens[index] : "";
        var columns = Enumerable.Range(0, width).Select(index => new GuideInterlinearColumn(At(words, index), At(morphemes, index), At(glosses, index))).ToArray();
        if (words.Count != morphemes.Count || morphemes.Count != glosses.Count)
            diagnostics.Add(new(GuideInterlinearDiagnosticKind.TokenCountMismatch,
                $"The word line has {words.Count} token(s), the morphemes line {morphemes.Count} and the gloss line {glosses.Count}; " +
                "they pair up one to one.", span, span.StartLine));
        else
            foreach (var column in columns.Where(column => column.MorphemePieces.Count != column.GlossPieces.Count))
                diagnostics.Add(new(GuideInterlinearDiagnosticKind.GlossMismatch,
                    $"\"{column.Morphemes}\" has {column.MorphemePieces.Count} morpheme(s) but \"{column.Gloss}\" has {column.GlossPieces.Count} gloss(es).",
                    span, span.StartLine));
        blocks.Add(new GuideInterlinearBlock(Clean(found[GuideInterlinearLabels.Word]), Clean(found[GuideInterlinearLabels.Morphemes]),
            Clean(found[GuideInterlinearLabels.Gloss]), found.TryGetValue(GuideInterlinearLabels.Translation, out var translation) ? Clean(translation) : null,
            columns, span));
    }

    private readonly record struct Line(int Start, int End, string Text, int Number);

    private static List<Line> Lines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r')) continue;
            lines.Add(new Line(start, i, text[start..i], lines.Count + 1));
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length || lines.Count == 0) lines.Add(new Line(start, text.Length, text[start..], lines.Count + 1));
        return lines;
    }
}
