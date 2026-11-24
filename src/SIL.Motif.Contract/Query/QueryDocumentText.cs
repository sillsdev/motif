using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query;

/// <summary>The text form of a whole <see cref="QueryDocument"/>: a <c>motif query</c> run line.</summary>
public static class QueryDocumentText
{
    /// <summary>Writes the canonical run line of a document; <see cref="TryParse"/> reads it back to the same document.</summary>
    public static string Print(QueryDocument document)
    {
        if (document.Members is not null)
            throw QueryRefusalException.DocumentInvalid("members is a file of ids in text; a document with members has no text form.", "members");
        QueryDocumentJson.RejectReserved(document.Where);
        var parts = new List<string> { "motif", "query" };
        if (document.Evidence) parts.Add("evidence");
        parts.Add(document.From);
        void Flag(string name, string? value)
        {
            if (value is null) return;
            parts.Add("--" + name);
            parts.Add(Quote(value));
        }
        if (document.Where is not null) Flag("where", PrintWhere(document.Where));
        if (document.Group.Count > 0) Flag("group", string.Join(",", document.Group));
        Flag("min-count", document.MinCount?.ToString(CultureInfo.InvariantCulture));
        Flag("examples", document.Examples?.ToString(CultureInfo.InvariantCulture));
        if (document.Ids) parts.Add("--ids");
        if (document.RawLetters) parts.Add("--raw-letters");
        Flag("trial", document.Trial);
        Flag("fold", document.Fold);
        Flag("distinct", document.Distinct);
        Flag("as-of", document.Side);
        Flag("sides", document.Sides);
        if (document.Sort is { Count: > 0 } sort)
        {
            if (sort.Count > 1) throw QueryRefusalException.DocumentInvalid("The text form sorts by one key.", "sort");
            Flag("sort", sort[0].Field + (sort[0].Descending ? ":desc" : ""));
        }
        Flag("limit", document.Limit?.ToString(CultureInfo.InvariantCulture));
        if (document.Score is not null) Flag("score", string.Join(",", document.Score));
        Flag("unit", document.Unit);
        Flag("segment-test", document.SegmentTest);
        foreach (var (name, fields) in new[] { ("exception", document.Exceptions), ("context", document.Context) })
            foreach (var field in fields ?? [])
            {
                var condition = QueryExprTools.ToCondition(field) ?? throw QueryRefusalException.DocumentInvalid(
                    "This list takes only eq, ne, in, has, lt, gt and contains predicates.", name);
                Flag(name, condition.Col + " " + condition.Op + " " + condition.Value);
            }
        if (document.Change is { } change)
        {
            Flag("change", change.Dropped + ">" + change.Gained);
            Flag("at", change.At);
            Flag("affixes", string.Join(",", change.Affixes));
            Flag("wordset", string.Join(",", change.WordSets));
            if (change.Discovery is not null) Flag("discovery", string.Join(",", change.Discovery));
            if (change.HeldOut is not null) Flag("heldout", string.Join(",", change.HeldOut));
            Flag("controls", change.Controls.ToString(CultureInfo.InvariantCulture));
            if (change.ControlLetters is not null) Flag("control-letters", string.Join(",", change.ControlLetters));
        }
        return string.Join(" ", parts);
    }

    // A lone predicate is written bare only when reading it as a v1 selector gives the same predicate.
    private static string PrintWhere(QueryExpr where)
    {
        var text = QueryExprText.Print(where);
        if (where is not QueryField field) return text;
        return QueryRunLine.TryParseSelector(text, out var condition, out var expression, out _) &&
               (condition is null ? expression is QueryField other && Same(other, field) : FromCondition(condition) is { } read && Same(read, field))
            ? text : "(" + text + ")";
    }

    private static bool Same(QueryField left, QueryField right) => left.Field == right.Field && left.Op == right.Op &&
        QueryDocumentJson.WriteExpr(left).ToJsonString() == QueryDocumentJson.WriteExpr(right).ToJsonString();

    private static string Quote(string value)
    {
        var bare = value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c is not ('"' or '\'' or ';' or '|' or '&' or '>' or '<' or '`' or '$' or '\\'));
        return bare ? value : "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>The predicate a v1 selector means.</summary>
    public static QueryField? FromCondition(QueryCondition condition)
    {
        string[] Split() => condition.Value.Split(',', StringSplitOptions.TrimEntries);
        return condition.Op switch
        {
            "is" => new QueryField(condition.Col, QueryOps.Eq, condition.Value),
            "not" => new QueryField(condition.Col, QueryOps.Ne, condition.Value),
            "in" => new QueryField(condition.Col, QueryOps.In, Split()),
            "has" => new QueryField(condition.Col, QueryOps.Has, Split()),
            "~" => new QueryField(condition.Col, QueryOps.Contains, condition.Value),
            "<" when long.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var below) =>
                new QueryField(condition.Col, QueryOps.Lt, below),
            ">" when long.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var above) =>
                new QueryField(condition.Col, QueryOps.Gt, above),
            _ => null,
        };
    }

    /// <summary>Reads a <c>motif query</c> line, v1 or v2, as the document it means.</summary>
    /// <param name="line">The run line, with or without the leading <c>motif</c>.</param>
    /// <param name="document">The document when the line is one.</param>
    /// <param name="refusal">Why it is not, otherwise.</param>
    public static bool TryParse(string line, [NotNullWhen(true)] out QueryDocument? document, [NotNullWhen(false)] out Refusal? refusal)
    {
        document = null;
        if (!QueryRunLine.TryTokenize(line, out var tokens, out refusal)) return false;
        var words = tokens.ToList();
        if (words.Count > 0 && words[0] == "motif") words.RemoveAt(0);
        if (words.Count == 0 || words[0] != QueryRunLine.QueryVerb)
        {
            refusal = Fail("A query line starts with motif query.");
            return false;
        }
        words.RemoveAt(0);
        if (!QueryRunLine.TryReadFlags(words, out var flags, out var positionals, out refusal)) return false;
        var evidence = positionals is ["evidence", ..];
        var from = evidence ? positionals.ElementAtOrDefault(1) : positionals.FirstOrDefault();
        if (positionals.Count != (evidence ? 2 : 1) || from is null || !QuerySources.All.Contains(from))
        {
            refusal = Fail("A query line names one source: forms, stems, uses or objects.");
            return false;
        }
        try
        {
            var filters = new List<QueryExpr>();
            foreach (var text in Repeated(words, "where"))
            {
                if (!QueryRunLine.TryParseSelector(text, out var condition, out var expression, out refusal)) return false;
                filters.Add(condition is null ? expression! : FromCondition(condition) ??
                    throw QueryRefusalException.DocumentInvalid("A selector's value does not fit its operator.", "where"));
            }
            QueryChangeDocument? change = null;
            if (flags.TryGetValue("change", out var changeText))
            {
                var arrow = changeText.IndexOf('>');
                if (arrow < 1) throw QueryRefusalException.DocumentInvalid("--change is <from>><to>.", "change");
                change = new QueryChangeDocument(changeText[..arrow], changeText[(arrow + 1)..], flags.GetValueOrDefault("at", "final"),
                    QueryRunLine.List(flags, "affixes"), QueryRunLine.List(flags, "wordset"),
                    flags.ContainsKey("discovery") ? QueryRunLine.List(flags, "discovery") : null,
                    flags.ContainsKey("heldout") ? QueryRunLine.List(flags, "heldout") : null,
                    Count(flags, "controls") ?? 5, flags.ContainsKey("control-letters") ? QueryRunLine.List(flags, "control-letters") : null);
            }
            List<QueryField> Fields(string name) => [.. Repeated(words, name).Select(text =>
                QueryRunLine.TryParseSelector(text, out var condition, out _, out _) && condition is not null && FromCondition(condition) is { } field
                    ? field : throw QueryRefusalException.DocumentInvalid("This takes one column, operator and value.", name))];
            var exceptions = Fields("exception");
            var context = Fields("context");
            document = new QueryDocument
            {
                From = from,
                Where = QueryExprTools.AllOf(filters),
                Side = flags.GetValueOrDefault("as-of"),
                Sides = flags.GetValueOrDefault("sides"),
                Sort = flags.TryGetValue("sort", out var sort)
                    ? [new QuerySortKey(sort.EndsWith(":desc", StringComparison.Ordinal) ? sort[..^5] : sort, sort.EndsWith(":desc", StringComparison.Ordinal))]
                    : null,
                Limit = Count(flags, "limit"),
                Group = QueryRunLine.List(flags, "group"),
                MinCount = Count(flags, "min-count"),
                Examples = Count(flags, "examples"),
                Ids = flags.ContainsKey("ids"),
                RawLetters = flags.ContainsKey("raw-letters"),
                Trial = flags.GetValueOrDefault("trial"),
                Fold = flags.GetValueOrDefault("fold"),
                Distinct = flags.GetValueOrDefault("distinct"),
                Score = flags.ContainsKey("score") ? QueryRunLine.List(flags, "score") : null,
                Unit = flags.GetValueOrDefault("unit"),
                SegmentTest = SegmentTestOf(flags),
                Evidence = evidence,
                Exceptions = exceptions.Count == 0 ? null : exceptions,
                Context = context.Count == 0 ? null : context,
                Change = change,
            };
            return true;
        }
        catch (QueryRefusalException error)
        {
            refusal = error.Refusal;
            return false;
        }
    }

    private static string? SegmentTestOf(IReadOnlyDictionary<string, string> flags)
    {
        if (!flags.TryGetValue("segment-test", out var text)) return null;
        SIL.Motif.Contract.Query.Phon.PhonSegmentTest.Normalize(text, "segment-test");
        return text;
    }

    private static int? Count(IReadOnlyDictionary<string, string> flags, string name) => flags.TryGetValue(name, out var text)
        ? int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
            : throw QueryRefusalException.DocumentInvalid($"--{name} is a whole number.", name)
        : null;

    private static List<string> Repeated(IReadOnlyList<string> words, string name)
    {
        var values = new List<string>();
        for (var i = 0; i + 1 < words.Count; i++)
            if (words[i] == "--" + name) values.Add(words[++i]);
        return values;
    }

    private static Refusal Fail(string message) => new(RefusalCodes.QueryDocumentInvalid, FailureReason.InvalidArgument, message);
}
