using System.Globalization;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Contract.Query;

/// <summary>One row as the engine sees it, whatever its source.</summary>
internal interface IQueryRowView
{
    string Id { get; }
    string SortKey { get; }
    long? Tokens { get; }
    IReadOnlyList<string> Get(string column);
}

/// <summary>What a source accepts: its columns, its set-valued columns and its numeric columns.</summary>
internal sealed record QuerySpec(Func<string, bool> IsColumn, Func<string, bool> IsSet, Func<string, bool> IsNumeric);

/// <summary>The groups and rows a pass selected, before a source projects them.</summary>
internal sealed record QueryOutcome(List<IQueryRowView> Selected, List<string> Ids, List<QueryOutcome.Group> Groups)
{
    internal sealed record Group(string[] Key, int Count, long? Tokens, List<IQueryRowView> Examples);
}

/// <summary>The one pass shared by every source: where, group, minimum count, distinct and examples.</summary>
internal static class QueryEngine
{
    public static QueryOutcome Execute(QueryRecord query, IEnumerable<IQueryRowView> rows, QuerySpec spec,
        Action<IQueryRowView>? observe = null, PhonSession? phon = null)
    {
        if (query.Where is null || query.Group is null || query.MinCount < 1 || query.Examples < 0 ||
            query.Examples > 1000 || query.Group.Count > 8) throw new ArgumentException("Invalid query.");
        if (query.Group.Any(c => !spec.IsColumn(c)) || query.Group.Distinct(StringComparer.Ordinal).Count() != query.Group.Count)
            throw new ArgumentException("Unknown or repeated grouping column.");
        if (query.Distinct is not null && (!spec.IsColumn(query.Distinct) || query.Group.Count == 0))
            throw new ArgumentException("Distinct needs a known column and a grouping.");
        var conditions = query.Where.Select(c => Compile(c, spec)).ToArray();
        Func<IQueryRowView, bool>? filter;
        if (QueryExprTools.HasPatterns(query.Filter))
        {
            if (phon is null) throw QueryRefusalException.NotYetAvailable("phon and env with this source or option");
            var materialised = rows as IReadOnlyList<IQueryRowView> ?? [.. rows];
            filter = Resolve(query, materialised, spec, conditions, phon);
            rows = materialised;
        }
        else filter = query.Filter is null ? null : QueryExprCompiler.Compile(query.Filter, spec);
        var selected = new List<IQueryRowView>();
        var ids = new List<string>();
        var groups = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (conditions.Any(c => !c(row)) || filter is not null && !filter(row)) continue;
            observe?.Invoke(row);
            if (query.Group.Count == 0)
            {
                if (query.Ids) ids.Add(row.Id); else selected.Add(row);
                continue;
            }
            var dimensions = new List<string[]>();
            long combinations = 1;
            foreach (var col in query.Group)
            {
                var parts = row.Get(col).Distinct(StringComparer.Ordinal).ToArray();
                dimensions.Add(parts);
                combinations *= parts.Length;
                if (combinations > 1000) throw new QueryTooManyGroupsException(string.Join(", ", query.Group));
            }
            var keys = new List<string[]> { Array.Empty<string>() };
            foreach (var parts in dimensions)
                keys = keys.SelectMany(key => parts.Select(part => key.Append(part).ToArray())).ToList();
            var distinct = query.Distinct is null ? null : row.Get(query.Distinct);
            foreach (var key in keys)
            {
                var encoded = key.Length == 1 ? key[0] : string.Join('\0', key);
                if (!groups.TryGetValue(encoded, out var group))
                {
                    if (groups.Count >= 100000) throw new QueryTooManyGroupsException(string.Join(", ", query.Group));
                    groups.Add(encoded, group = new GroupBuilder(key));
                }
                group.Count++;
                group.Tokens = group.Tokens is { } total && row.Tokens is { } count ? total + count : null;
                if (query.Ids) group.Ids.Add(row.Id);
                if (distinct is not null) foreach (var value in distinct) group.DistinctValues.Add(value);
                if (query.Examples > 0)
                {
                    group.Examples.Enqueue(row, (row.SortKey, row.Id));
                    if (group.Examples.Count > query.Examples) group.Examples.Dequeue();
                }
            }
        }
        var kept = groups.Values.Where(g => g.Count >= query.MinCount && (query.Distinct is null || g.DistinctValues.Count >= 2)).ToArray();
        if (query.Ids) ids.AddRange(kept.SelectMany(g => g.Ids));
        var result = kept.OrderByDescending(g => g.Count).ThenBy(g => string.Join("\0", g.Key), StringComparer.Ordinal)
            .Select(g => new QueryOutcome.Group(g.Key, g.Count, g.Tokens, [.. g.Examples.UnorderedItems.Select(x => x.Element)
                .OrderBy(x => x.SortKey, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal)])).ToList();
        return new QueryOutcome(selected, [.. ids.Distinct(StringComparer.Ordinal)], result);
    }

    // Cheaper predicates run first; PanGloss sees only the forms left undecided, once per pattern, in batches.
    private static Func<IQueryRowView, bool> Resolve(QueryRecord query, IReadOnlyList<IQueryRowView> rows, QuerySpec spec,
        Func<IQueryRowView, bool>[] conditions, PhonSession phon)
    {
        var patterns = QueryExprTools.Patterns(query.Filter);
        var optimistic = QueryExprCompiler.CompileTri(query.Filter!, spec, (_, _) => Tri.Unknown);
        var candidates = rows.Where(row => conditions.All(c => c(row)) && optimistic(row) != Tri.False)
            .Select(row => row.Get("form") is { Count: > 0 } form ? form[0] : "").Where(form => form.Length > 0);
        phon.Resolve([.. patterns.Select(pattern => pattern.Leaf)], candidates);
        phon.Reported(patterns.Where(pattern => pattern.Positive).Select(pattern => pattern.Leaf));
        var decided = QueryExprCompiler.CompileTri(query.Filter!, spec, phon.Lookup);
        return row => decided(row) == Tri.True;
    }

    internal static Func<IQueryRowView, bool> Compile(QueryCondition condition, QuerySpec spec)
    {
        if (condition is null || condition.Col is null || condition.Op is null || condition.Value is null ||
            !spec.IsColumn(condition.Col)) throw new ArgumentException("Unknown query column.");
        if (condition.Op == "has" && !spec.IsSet(condition.Col))
            throw new ArgumentException("The has operator requires a set column.");
        var numeric = spec.IsNumeric(condition.Col);
        if (condition.Op is "<" or ">" && !numeric)
            throw new ArgumentException("Numeric comparison requires a numeric column.");
        Regex? regex = null;
        if (condition.Op == "~")
        {
            if (numeric) throw new ArgumentException("Regular expressions require a string column.");
            if (condition.Value.Length > FormQuery.MaximumRegexLength) throw new ArgumentException("Regular expression is too long.");
            try { regex = new Regex(condition.Value, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException)
            { throw new ArgumentException("Invalid non-backtracking regular expression.", e); }
        }
        if (condition.Op is not ("is" or "in" or "not" or "~" or "has" or "<" or ">"))
            throw new ArgumentException("Unknown query operator.");
        var targets = condition.Value.Split(',', StringSplitOptions.TrimEntries);
        long limit = 0;
        if (condition.Op is "<" or ">" && !long.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit))
            throw new ArgumentException("Expected an integer.");
        return row =>
        {
            var items = row.Get(condition.Col);
            return condition.Op switch
            {
                "is" => items.Contains(condition.Value, StringComparer.Ordinal),
                "not" => !items.Contains(condition.Value, StringComparer.Ordinal),
                "in" or "has" => items.Any(x => targets.Contains(x, StringComparer.Ordinal)),
                "~" => items.Any(x => regex!.IsMatch(x)),
                "<" => items.Count > 0 && long.TryParse(items[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v < limit,
                ">" => items.Count > 0 && long.TryParse(items[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > limit,
                _ => false
            };
        };
    }

    private sealed class GroupBuilder(string[] key)
    {
        public string[] Key { get; } = key;
        public int Count { get; set; }
        public long? Tokens { get; set; } = 0;
        public List<string> Ids { get; } = [];
        public HashSet<string> DistinctValues { get; } = new(StringComparer.Ordinal);
        public PriorityQueue<IQueryRowView, (string, string)> Examples { get; } = new(
            Comparer<(string, string)>.Create((a, b) => StringComparer.Ordinal.Compare(b.Item1, a.Item1) is var c && c != 0
                ? c : StringComparer.Ordinal.Compare(b.Item2, a.Item2)));
    }
}
