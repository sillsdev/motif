using System.Globalization;
using SIL.Motif.Contract.Query.Corpus;

namespace SIL.Motif.Contract.Gdr;

/// <summary>
/// A corpus-forms answer shaped as a Guide block shows it: named columns, string cells, and a headline that says "at least" when the
/// parse behind the counts is incomplete (ADR 0005). It is computed from a run and never stored.
/// </summary>
/// <param name="Columns">The column names shown, in order.</param>
/// <param name="Rows">The cells of each row, aligned with <paramref name="Columns"/>.</param>
/// <param name="Headline">The count sentence, such as "at least 412 word types (1,300 tokens)" with the reason.</param>
/// <param name="Complete">False when the counts are lower bounds.</param>
/// <param name="Why">Why the counts are lower bounds; null when complete.</param>
/// <param name="Types">The matching word types (or words with several readings for a cause table).</param>
/// <param name="Tokens">Their tokens, when the run totalled them.</param>
/// <param name="Truncated">Rows the answer held beyond those shown by a limit.</param>
/// <param name="Capped">Words the parse stopped at the work limit (0 when the query read no parse).</param>
/// <param name="TimedOut">Words stopped at the word timeout.</param>
/// <param name="Failed">Words the parse gave no answer for.</param>
public sealed record GuideEvidenceTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string Headline, bool Complete,
    string? Why, long Types, long? Tokens, int Truncated, int Capped = 0, int TimedOut = 0, int Failed = 0)
{
    /// <summary>The word types of the answer written as a count, with "at least" when it is a lower bound.</summary>
    public string CountText => (Complete ? "" : "at least ") + Types.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Every column a query with this request can show, in the order a table lists them by default.</summary>
    public static IReadOnlyList<string> ColumnsFor(CorpusFormsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var groups = GroupColumns(request);
        if (groups is not null) return groups == Cause ? ["cause", "types", "tokens", "benign", "example"] : [.. groups, "types", "tokens"];
        var columns = new List<string> { "form", "tokens", "parse.state", "readings" };
        if (request.BaselineGrammarPath is not null) columns.AddRange(BaselineSet.Columns);
        var twins = request.Twin?.Count ?? 0;
        for (var i = 1; i <= twins; i++)
            foreach (var part in TwinSet.ColumnParts) columns.Add((twins == 1 ? "twin" : "twin" + i.ToString(CultureInfo.InvariantCulture)) + "." + part);
        return columns;
    }

    private static readonly string[] Cause = ["cause"];

    private static string[]? GroupColumns(CorpusFormsRequest request) =>
        request.GroupBy is null ? null : request.GroupBy.Trim() == "cause" ? Cause
            : request.GroupBy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>What a block shows when it names no columns: the first three and any twin or change column that has values.</summary>
    public static IReadOnlyList<string> DefaultColumns(CorpusFormsRequest request, CorpusFormsResponse? response)
    {
        var all = ColumnsFor(request);
        if (GroupColumns(request) is not null) return all;
        var rows = response?.Rows ?? [];
        return [.. all.Where(column => column switch
        {
            "form" or "tokens" => true,
            "parse.state" => rows.Any(row => row.ParseState is not null),
            "readings" => rows.Any(row => row.Readings is not null),
            "change" or "base.parse.state" => true,
            _ => column.StartsWith("twin", StringComparison.Ordinal) && (column.EndsWith(".form", StringComparison.Ordinal) || column.EndsWith(".parse.state", StringComparison.Ordinal)),
        })];
    }

    /// <summary>The columns of <paramref name="names"/> that the request cannot show; empty when every one is known.</summary>
    public static IReadOnlyList<string> Unknown(CorpusFormsRequest request, IEnumerable<string> names)
    {
        var known = ColumnsFor(request);
        return [.. names.Where(name => !known.Contains(name, StringComparer.Ordinal))];
    }

    /// <summary>Shapes <paramref name="response"/> for a block: the chosen columns (all defaults when null), sorted, limited.</summary>
    public static GuideEvidenceTable Build(CorpusFormsRequest request, CorpusFormsResponse response, IReadOnlyList<string>? columns, GuideSort? sort, int? limit)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        var (complete, why) = Completeness(response);
        var chosen = columns is { Count: > 0 } ? columns : DefaultColumns(request, response);
        var cells = new List<IReadOnlyList<string>>();
        long types;
        long? tokens = response.Totals?.Tokens;
        string noun;
        if (response.Causes is { } causes)
        {
            types = causes.Words;
            tokens = causes.Tokens;
            noun = "words with more than one reading";
            foreach (var group in causes.Groups)
                cells.Add(chosen.Select(column => column switch
                {
                    "cause" => group.Cause + (group.Detail is null ? "" : "[" + group.Detail + "]"),
                    "types" => Number(group.Words),
                    "tokens" => Number(group.Tokens),
                    "benign" => group.Benign ? "benign" : "",
                    "example" => group.Example.Word,
                    _ => "",
                }).ToArray());
        }
        else if (response.Groups is { } groups)
        {
            types = response.Totals?.Types ?? response.Matched;
            noun = "word types in " + Number(groups.Groups.Count) + (groups.Groups.Count == 1 ? " group" : " groups");
            foreach (var group in groups.Groups)
                cells.Add(chosen.Select(column =>
                {
                    var at = groups.Columns.ToList().IndexOf(column);
                    return at >= 0 && at < group.Values.Count ? group.Values[at] : column switch { "types" => Number(group.Types), "tokens" => Number(group.Tokens), _ => "" };
                }).ToArray());
        }
        else
        {
            types = response.Matched;
            noun = "word types";
            foreach (var row in response.Rows) cells.Add(chosen.Select(column => Cell(row, column)).ToArray());
        }
        if (sort is not null)
        {
            var at = chosen.ToList().IndexOf(sort.Column);
            if (at >= 0)
            {
                var ordered = cells.OrderBy(row => row[at], Comparer<string>.Create(Compare));
                cells = [.. sort.Descending ? cells.OrderByDescending(row => row[at], Comparer<string>.Create(Compare)) : ordered];
            }
        }
        var truncated = 0;
        if (limit is { } max && cells.Count > max)
        {
            truncated = cells.Count - max;
            cells = [.. cells.Take(max)];
        }
        var headline = (complete ? "" : "at least ") + Number(types) + " " + noun + (tokens is { } total ? " (" + (complete ? "" : "at least ") + Number(total) + " tokens)" : "");
        if (why is not null) headline += ": " + why;
        var info = response.ReadingsInfo;
        return new GuideEvidenceTable(chosen, cells, headline, complete, why, types, tokens, truncated, info?.Capped ?? 0, info?.TimedOut ?? 0, info?.Failed ?? 0);
    }

    private static (bool Complete, string? Why) Completeness(CorpusFormsResponse response)
    {
        var infos = new[] { response.ReadingsInfo, response.BaselineInfo }.Where(info => info is not null).Cast<ReadingsReport>().ToList();
        var incomplete = infos.FirstOrDefault(info => !info.Complete);
        if (incomplete is null)
            return response.Causes is { Complete: false } ? (false, "the parse is incomplete; the counts are lower bounds") : (true, null);
        var parts = new List<string>();
        if (incomplete.Capped > 0) parts.Add(Number(incomplete.Capped) + " words stopped at the parser's work limit");
        if (incomplete.TimedOut > 0) parts.Add(Number(incomplete.TimedOut) + " timed out");
        if (incomplete.Failed > 0) parts.Add(Number(incomplete.Failed) + " got no answer");
        if (incomplete.UnknownMsa > 0) parts.Add(Number(incomplete.UnknownMsa) + " analysis ids are unknown to the project");
        return (false, "the parse is incomplete (" + (parts.Count == 0 ? incomplete.Note ?? "see the query's readingsInfo" : string.Join(", ", parts)) + "); the counts are lower bounds");
    }

    private static string Cell(CorpusFormRow row, string column)
    {
        switch (column)
        {
            case "form": return row.Form;
            case "tokens": return Number(row.Tokens);
            case "parse.state": return row.ParseState ?? "";
            case "readings": return row.Readings is { } readings ? Number(readings) : "";
            case "change": return row.Baseline?.Change ?? "";
            case "base.parse.state": return row.Baseline?.ParseState ?? "";
            case "base.readings": return row.Baseline is { } baseline ? Number(baseline.Readings) : "";
        }
        if (!TwinSet.TryColumn(column, out var index, out var part) || row.Twins is not { } twins || index >= twins.Count) return "";
        var twin = twins[index];
        return part switch
        {
            "form" => twin.Form,
            "tokens" => Number(twin.Tokens),
            "inCorpus" => twin.InCorpus ? "yes" : "no",
            "parse.state" => twin.ParseState ?? "",
            "readings" => twin.Readings is { } count ? Number(count) : "",
            _ => "",
        };
    }

    private static int Compare(string left, string right) =>
        long.TryParse(left, NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var a) &&
        long.TryParse(right, NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b)
            ? a.CompareTo(b) : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
