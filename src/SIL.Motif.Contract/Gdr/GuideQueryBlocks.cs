using System.Globalization;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;

namespace SIL.Motif.Contract.Gdr;

/// <summary>Where a fenced block sits in the Markdown it came from; offsets count UTF-16 chars and lines count from 1.</summary>
/// <param name="Start">Offset of the first char of the opening fence line.</param>
/// <param name="End">Offset just past the last char of the closing fence, before its line break; the end of text when unclosed.</param>
/// <param name="StartLine">The line of the opening fence.</param>
/// <param name="EndLine">The line of the closing fence.</param>
/// <param name="BodyStart">Offset of the first body line, which equals <paramref name="BodyEnd"/> for an empty body.</param>
/// <param name="BodyEnd">Offset of the line break before the closing fence, or of <paramref name="End"/> when unclosed.</param>
public sealed record GuideBlockSpan(int Start, int End, int StartLine, int EndLine, int BodyStart, int BodyEnd);

/// <summary>A query's sort: a column and its direction.</summary>
public sealed record GuideSort(string Column, bool Descending);

/// <summary>How a <c>motif-show</c> directive renders its query.</summary>
public enum GuideShowKind
{
    Table,
    Counts,
    List,
}

/// <summary>One <c>motif-query</c> block; <see cref="Expect"/> is null when the block declares no count.</summary>
public sealed record GuideQueryDefinition(string Id, string Title, string Run, IReadOnlyList<string> Columns, GuideSort? Sort,
    GuideBlockSpan Span, GuideExpectedCount? Expect = null);

/// <summary>One <c>motif-show</c> block; <see cref="Limit"/> and <see cref="Columns"/> are null when the block leaves them out.</summary>
public sealed record GuideShowDirective(string Query, GuideShowKind Show, int? Limit, IReadOnlyList<string>? Columns,
    GuideBlockSpan Span);

/// <summary>The kinds of problem a block can have.</summary>
public enum GuideBlockDiagnosticKind
{
    /// <summary>The block's closing fence is missing.</summary>
    UnclosedBlock,
    /// <summary>A body line is not <c>key: value</c>.</summary>
    MalformedLine,
    /// <summary>A key the block kind does not define; the block is otherwise kept.</summary>
    UnknownField,
    /// <summary>A key appears twice in one block; the first value is kept.</summary>
    DuplicateField,
    /// <summary>A required key is absent or empty.</summary>
    MissingField,
    /// <summary>A value is not what its key takes.</summary>
    MalformedField,
    /// <summary>A query id is not lowercase letters, digits and hyphens.</summary>
    InvalidId,
    /// <summary>A query id already names an earlier block.</summary>
    DuplicateId,
    /// <summary>A show directive's query names no defined id.</summary>
    UnknownQuery,
    /// <summary>A query's run line is not a read-only <c>motif query</c>, <c>motif query corpus forms</c> or <c>motif group</c> line.</summary>
    InvalidRun,
    /// <summary>A <c>columns</c> or <c>sort</c> names a column the query's v2 answer does not have.</summary>
    UnknownColumn,
}

/// <summary>One problem found in a block, typed so a caller can place it and branch on it.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Message">A sentence a person reads.</param>
/// <param name="Span">The block it is in.</param>
/// <param name="Line">The 1-based line of the offending field, or the block's first line.</param>
/// <param name="Field">The key involved, when there is one.</param>
/// <param name="RefusalCode">The run-line refusal code for <see cref="GuideBlockDiagnosticKind.InvalidRun"/>.</param>
public sealed record GuideBlockDiagnostic(GuideBlockDiagnosticKind Kind, string Message, GuideBlockSpan Span, int Line,
    string? Field = null, string? RefusalCode = null);

/// <summary>Everything the parser found, in document order.</summary>
public sealed record GuideBlocks(IReadOnlyList<GuideQueryDefinition> Queries, IReadOnlyList<GuideShowDirective> Shows,
    IReadOnlyList<GuideBlockDiagnostic> Diagnostics);

/// <summary>Finds the <c>motif-query</c> and <c>motif-show</c> blocks of a Guide section or Proposal and never throws on bad input.</summary>
public static class GuideQueryBlocks
{
    private static readonly string[] QueryKeys = ["id", "title", "run", "columns", "sort", "expect"];
    private static readonly string[] ShowKeys = ["query", "show", "limit", "columns"];

    /// <summary>Parses the Markdown; a block with a problem is still returned where it has enough to place, with a diagnostic.</summary>
    public static GuideBlocks Parse(string? markdown)
    {
        markdown ??= "";
        var queries = new List<GuideQueryDefinition>();
        var shows = new List<GuideShowDirective>();
        var diagnostics = new List<GuideBlockDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = Lines(markdown);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!TryFence(markdown, lines[i], out var fenceChar, out var fenceLength, out var info)) continue;
            var close = -1;
            for (var j = i + 1; j < lines.Count; j++)
                if (IsClosing(markdown, lines[j], fenceChar, fenceLength)) { close = j; break; }
            var tag = info.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var last = close < 0 ? lines.Count - 1 : close;
            if (tag is GdrTemplate.QueryBlockTag or GdrTemplate.ShowBlockTag)
            {
                var bodyEnd = close < 0 ? lines.Count : close;
                var hasBody = bodyEnd > i + 1;
                var span = new GuideBlockSpan(lines[i].Start, lines[last].End, i + 1, last + 1,
                    hasBody ? lines[i + 1].Start : lines[i].End, hasBody ? lines[bodyEnd - 1].End : lines[i].End);
                if (close < 0)
                    diagnostics.Add(new(GuideBlockDiagnosticKind.UnclosedBlock, "The block has no closing fence.", span, i + 1));
                var body = lines.Skip(i + 1).Take(Math.Max(0, (close < 0 ? lines.Count : close) - i - 1)).ToList();
                if (tag == GdrTemplate.QueryBlockTag) ParseQuery(markdown, body, i + 2, span, queries, diagnostics, seen);
                else ParseShow(markdown, body, i + 2, span, shows, diagnostics);
            }
            if (close < 0) break;
            i = close;
        }
        foreach (var show in shows)
        {
            if (!seen.Contains(show.Query))
                diagnostics.Add(new(GuideBlockDiagnosticKind.UnknownQuery, $"No query has the id \"{show.Query}\".", show.Span,
                    show.Span.StartLine, "query"));
            else if (show.Columns is { Count: > 0 } && queries.FirstOrDefault(query => query.Id == show.Query) is { } shown &&
                     QueryRunLine.TryParseRun(shown.Run, out var shownRun, out _) && shownRun.Corpus is { } shownCorpus)
                ReportUnknown(shownCorpus, show.Columns, "columns", show.Span, show.Span.StartLine, diagnostics);
        }
        return new GuideBlocks(queries, shows, [.. diagnostics.OrderBy(d => d.Line)]);
    }

    /// <summary>
    /// The span of every <c>motif-query</c> and <c>motif-show</c> fence in the Markdown, in order, including those too
    /// malformed for <see cref="Parse"/> to return as a query or show.
    /// </summary>
    public static IReadOnlyList<GuideBlockSpan> Spans(string? markdown)
    {
        markdown ??= "";
        var spans = new List<GuideBlockSpan>();
        var lines = Lines(markdown);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!TryFence(markdown, lines[i], out var fenceChar, out var fenceLength, out var info)) continue;
            var close = -1;
            for (var j = i + 1; j < lines.Count; j++)
                if (IsClosing(markdown, lines[j], fenceChar, fenceLength)) { close = j; break; }
            var tag = info.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var last = close < 0 ? lines.Count - 1 : close;
            if (tag is GdrTemplate.QueryBlockTag or GdrTemplate.ShowBlockTag)
            {
                var bodyEnd = close < 0 ? lines.Count : close;
                var hasBody = bodyEnd > i + 1;
                spans.Add(new GuideBlockSpan(lines[i].Start, lines[last].End, i + 1, last + 1,
                    hasBody ? lines[i + 1].Start : lines[i].End, hasBody ? lines[bodyEnd - 1].End : lines[i].End));
            }
            if (close < 0) break;
            i = close;
        }
        return spans;
    }

    /// <summary>Replaces one block's text; every char outside the span is kept as it was. A span is valid only for the text it came from, so parse again after each replacement.</summary>
    public static string Replace(string markdown, GuideBlockSpan span, string replacement)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(replacement);
        if (span.Start < 0 || span.End < span.Start || span.End > markdown.Length)
            throw new ArgumentOutOfRangeException(nameof(span), "The span does not lie inside the text.");
        return string.Concat(markdown.AsSpan(0, span.Start), replacement, markdown.AsSpan(span.End));
    }

    private static void ParseQuery(string markdown, List<Line> body, int firstLine, GuideBlockSpan span,
        List<GuideQueryDefinition> queries, List<GuideBlockDiagnostic> diagnostics, HashSet<string> seen)
    {
        var fields = ReadFields(markdown, body, firstLine, span, QueryKeys, diagnostics);
        string Value(string key) => fields.TryGetValue(key, out var field) ? field.Value : "";
        int LineOf(string key) => fields.TryGetValue(key, out var field) ? field.Line : span.StartLine;
        foreach (var key in new[] { "id", "run" })
            if (Value(key).Length == 0)
                diagnostics.Add(new(GuideBlockDiagnosticKind.MissingField, $"A query needs {key}.", span, span.StartLine, key));
        if (Value("title").Length == 0)
            diagnostics.Add(new(GuideBlockDiagnosticKind.MissingField, "A query needs title.", span, span.StartLine, "title"));
        var id = Value("id");
        if (id.Length > 0 && !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            diagnostics.Add(new(GuideBlockDiagnosticKind.InvalidId, "An id is lowercase letters, digits and hyphens.", span, LineOf("id"), "id"));
        if (id.Length > 0 && !seen.Add(id))
            diagnostics.Add(new(GuideBlockDiagnosticKind.DuplicateId, $"The id \"{id}\" is already used in this text.", span, LineOf("id"), "id"));
        var run = Value("run");
        if (run.Length > 0 && !QueryRunLine.TryParseRun(run, out _, out var refusal))
            diagnostics.Add(new(GuideBlockDiagnosticKind.InvalidRun, refusal.Message, span, LineOf("run"), "run", refusal.Code));
        CorpusFormsRequest? corpus = null;
        if (run.Length > 0 && QueryRunLine.TryParseRun(run, out var parsedRun, out _)) corpus = parsedRun.Corpus;
        if (corpus is not null && fields.ContainsKey("columns"))
            ReportUnknown(corpus, Split(Value("columns")), "columns", span, LineOf("columns"), diagnostics);
        GuideSort? sort = null;
        if (fields.ContainsKey("sort"))
        {
            var parts = Value("sort").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 1 or 2 && (parts.Length == 1 || parts[1] is "asc" or "desc"))
                sort = new GuideSort(parts[0], parts.Length == 2 && parts[1] == "desc");
            else diagnostics.Add(new(GuideBlockDiagnosticKind.MalformedField, "sort is a column, then asc or desc.", span, LineOf("sort"), "sort"));
            if (corpus is not null && sort is not null) ReportUnknown(corpus, [sort.Column], "sort", span, LineOf("sort"), diagnostics);
        }
        GuideExpectedCount? expect = null;
        if (fields.ContainsKey("expect") && !GuideExpectedCount.TryParse(Value("expect"), out expect))
            diagnostics.Add(new(GuideBlockDiagnosticKind.MalformedField,
                "expect is a count (127), a range (100..150) or a bound (>=1, >1, <=5, <5).", span, LineOf("expect"), "expect"));
        if (id.Length > 0 || run.Length > 0)
            queries.Add(new GuideQueryDefinition(id, Value("title"), run, Split(Value("columns")), sort, span, expect));
    }

    // A column a v2 corpus query cannot show is a diagnostic on the block, never a crash at render.
    private static void ReportUnknown(CorpusFormsRequest corpus, IEnumerable<string> names, string field, GuideBlockSpan span, int line,
        List<GuideBlockDiagnostic> diagnostics)
    {
        var unknown = GuideEvidenceTable.Unknown(corpus, names);
        if (unknown.Count > 0)
            diagnostics.Add(new(GuideBlockDiagnosticKind.UnknownColumn,
                $"This query has no column {string.Join(", ", unknown.Select(name => "\"" + name + "\""))}; it has {string.Join(", ", GuideEvidenceTable.ColumnsFor(corpus))}.",
                span, line, field));
    }

    private static void ParseShow(string markdown, List<Line> body, int firstLine, GuideBlockSpan span,
        List<GuideShowDirective> shows, List<GuideBlockDiagnostic> diagnostics)
    {
        var fields = ReadFields(markdown, body, firstLine, span, ShowKeys, diagnostics);
        string Value(string key) => fields.TryGetValue(key, out var field) ? field.Value : "";
        int LineOf(string key) => fields.TryGetValue(key, out var field) ? field.Line : span.StartLine;
        var query = Value("query");
        if (query.Length == 0)
            diagnostics.Add(new(GuideBlockDiagnosticKind.MissingField, "A show needs query.", span, span.StartLine, "query"));
        var kind = GuideShowKind.Table;
        if (fields.ContainsKey("show"))
        {
            switch (Value("show"))
            {
                case "table": break;
                case "counts": kind = GuideShowKind.Counts; break;
                case "list": kind = GuideShowKind.List; break;
                default:
                    diagnostics.Add(new(GuideBlockDiagnosticKind.MalformedField, "show is table, counts or list.", span, LineOf("show"), "show"));
                    break;
            }
        }
        int? limit = null;
        if (fields.ContainsKey("limit"))
        {
            if (int.TryParse(Value("limit"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1) limit = number;
            else diagnostics.Add(new(GuideBlockDiagnosticKind.MalformedField, "limit is a positive number of rows.", span, LineOf("limit"), "limit"));
        }
        if (query.Length > 0)
            shows.Add(new GuideShowDirective(query, kind, limit, fields.ContainsKey("columns") ? Split(Value("columns")) : null, span));
    }

    private static Dictionary<string, (string Value, int Line)> ReadFields(string markdown, List<Line> body, int firstLine,
        GuideBlockSpan span, string[] keys, List<GuideBlockDiagnostic> diagnostics)
    {
        var fields = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        for (var i = 0; i < body.Count; i++)
        {
            var text = markdown.Substring(body[i].Start, body[i].End - body[i].Start).Trim();
            if (text.Length == 0) continue;
            var line = firstLine + i;
            var colon = text.IndexOf(':');
            if (colon < 1)
            {
                diagnostics.Add(new(GuideBlockDiagnosticKind.MalformedLine, "A line is key: value.", span, line));
                continue;
            }
            var key = text[..colon].Trim();
            var value = text[(colon + 1)..].Trim();
            if (!keys.Contains(key))
                diagnostics.Add(new(GuideBlockDiagnosticKind.UnknownField, $"The key \"{key}\" is not used here.", span, line, key));
            else if (!fields.TryAdd(key, (value, line)))
                diagnostics.Add(new(GuideBlockDiagnosticKind.DuplicateField, $"The key \"{key}\" appears twice.", span, line, key));
        }
        return fields;
    }

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private readonly record struct Line(int Start, int End);

    private static List<Line> Lines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r')) continue;
            lines.Add(new Line(start, i));
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length || lines.Count == 0) lines.Add(new Line(start, text.Length));
        return lines;
    }

    private static bool TryFence(string text, Line line, out char fenceChar, out int length, out string info)
    {
        fenceChar = '\0';
        length = 0;
        info = "";
        var i = line.Start;
        while (i < line.End && i - line.Start < 4 && text[i] == ' ') i++;
        if (i - line.Start > 3 || i >= line.End || text[i] is not ('`' or '~')) return false;
        fenceChar = text[i];
        var run = i;
        while (run < line.End && text[run] == fenceChar) run++;
        length = run - i;
        if (length < 3) return false;
        info = text.Substring(run, line.End - run).Trim();
        return fenceChar != '`' || !info.Contains('`');
    }

    private static bool IsClosing(string text, Line line, char fenceChar, int minimum)
    {
        var i = line.Start;
        while (i < line.End && i - line.Start < 4 && text[i] == ' ') i++;
        if (i - line.Start > 3) return false;
        var run = i;
        while (run < line.End && text[run] == fenceChar) run++;
        return run - i >= minimum && text.AsSpan(run, line.End - run).Trim().IsEmpty;
    }
}
