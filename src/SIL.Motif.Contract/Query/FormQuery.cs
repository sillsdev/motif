using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Contract.Query;

/// <summary>A grouping request exceeded its bounded result size.</summary>
public sealed class QueryTooManyGroupsException(string columns) : ArgumentException($"Too many groups for columns: {columns}.");

/// <summary>Evaluates the forms query over a caller-owned immutable snapshot.</summary>
public static class FormQuery
{
    /// <summary>The maximum accepted regular expression length.</summary>
    public const int MaximumRegexLength = 256;
    private static readonly HashSet<string> Columns = new(StringComparer.Ordinal)
    {
        "id", "form", "base", "kind", "entry", "morph-type", "variant-type", "environments", "category",
        "tokens", "change", "change.dropped", "change.gained", "change.at", "change.left", "change.right",
        "change.abstract", "letters.dropped", "letters.gained"
    };
    private static readonly HashSet<string> Sets = new(StringComparer.Ordinal)
        { "environments", "category", "letters.dropped", "letters.gained" };

    /// <summary>Validates the complete record, then returns matching rows or grouped counts.</summary>
    public static QueryResult Run(QueryRecord query, IReadOnlyList<QueryForm> rows, IReadOnlyList<string> graphemes)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(graphemes);
        if (query.From != "forms" || query.Where is null || query.Group is null || query.MinCount < 1 ||
            query.Examples < 0 || query.Examples > 1000 || query.Group.Count > 8)
            throw new ArgumentException("Invalid forms query.");
        if (query.Group.Any(c => !Columns.Contains(c)) || query.Group.Distinct(StringComparer.Ordinal).Count() != query.Group.Count)
            throw new ArgumentException("Unknown or repeated grouping column.");
        var conditions = query.Where.Select(Compile).ToArray();
        var segments = graphemes.Where(g => !string.IsNullOrEmpty(g)).Distinct(StringComparer.Ordinal)
            .OrderByDescending(g => g.Length).ThenBy(g => g, StringComparer.Ordinal).ToArray();
        var segmentIndex = (query.RawLetters ? [] : segments).GroupBy(g => g[0]).ToDictionary(g => g.Key, g => g.ToArray());
        var selected = new List<QueryForm>();
        var ids = new List<string>();
        var groups = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var values = new FormValues(row, segmentIndex);
            if (conditions.Any(c => !c(values))) continue;
            if (query.Group.Count == 0)
            {
                if (query.Ids) ids.Add(row.Id);
                if (!query.Ids) selected.Add(values.Project());
                continue;
            }
            var dimensions = new List<string[]>();
            long combinations = 1;
            foreach (var col in query.Group)
            {
                var parts = values.Get(col).Distinct(StringComparer.Ordinal).ToArray();
                dimensions.Add(parts);
                combinations *= parts.Length;
                if (combinations > 1000) throw new QueryTooManyGroupsException(string.Join(", ", query.Group));
            }
            var keys = new List<string[]> { Array.Empty<string>() };
            foreach (var parts in dimensions)
            {
                keys = keys.SelectMany(key => parts.Select(part => key.Append(part).ToArray())).ToList();
            }
            foreach (var key in keys)
            {
                var encoded = System.Text.Json.JsonSerializer.Serialize(key);
                if (!groups.TryGetValue(encoded, out var group))
                {
                    if (groups.Count >= 100000) throw new QueryTooManyGroupsException(string.Join(", ", query.Group));
                    groups.Add(encoded, group = new GroupBuilder(key));
                }
                group.Count++;
                group.Tokens = group.Tokens is { } total && row.Tokens is { } count ? total + count : null;
                if (query.Ids) group.Ids.Add(row.Id);
                if (query.Examples > 0)
                {
                    group.Examples.Enqueue(values, (row.Form, row.Id));
                    if (group.Examples.Count > query.Examples) group.Examples.Dequeue();
                }
            }
        }
        if (query.Ids) ids.AddRange(groups.Values.Where(g => g.Count >= query.MinCount).SelectMany(g => g.Ids));
        var result = groups.Values.Where(g => g.Count >= query.MinCount)
            .OrderByDescending(g => g.Count).ThenBy(g => string.Join("\0", g.Key), StringComparer.Ordinal)
            .Select(g => new QueryGroup(g.Key, g.Count, g.Tokens, g.Examples.UnorderedItems.Select(x => x.Element.Project())
                .OrderBy(x => x.Form, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray())).ToArray();
        return new QueryResult(selected, result, ids.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static Func<FormValues, bool> Compile(QueryCondition condition)
    {
        if (condition is null || condition.Col is null || condition.Op is null || condition.Value is null ||
            !Columns.Contains(condition.Col)) throw new ArgumentException("Unknown query column.");
        if (condition.Op == "has" && !Sets.Contains(condition.Col))
            throw new ArgumentException("The has operator requires a set column.");
        if (condition.Op is "<" or ">" && condition.Col != "tokens")
            throw new ArgumentException("Numeric comparison requires a numeric column.");
        Regex? regex = null;
        if (condition.Op == "~")
        {
            if (condition.Col == "tokens") throw new ArgumentException("Regular expressions require a string column.");
            if (condition.Value.Length > MaximumRegexLength) throw new ArgumentException("Regular expression is too long.");
            try { regex = new Regex(condition.Value, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException)
            { throw new ArgumentException("Invalid non-backtracking regular expression.", e); }
        }
        if (condition.Op is not ("is" or "in" or "not" or "~" or "has" or "<" or ">"))
            throw new ArgumentException("Unknown query operator.");
        var targets = condition.Value.Split(',', StringSplitOptions.TrimEntries);
        if (condition.Op is "<" or ">" && !long.TryParse(condition.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out _)) throw new ArgumentException("Expected an integer.");
        return values =>
        {
            var items = values.Get(condition.Col);
            return condition.Op switch
            {
                "is" => items.Contains(condition.Value, StringComparer.Ordinal),
                "not" => !items.Contains(condition.Value, StringComparer.Ordinal),
                "in" or "has" => items.Any(x => targets.Contains(x, StringComparer.Ordinal)),
                "~" => items.Any(x => regex!.IsMatch(x)),
                "<" => values.Row.Tokens < long.Parse(condition.Value, CultureInfo.InvariantCulture),
                ">" => values.Row.Tokens > long.Parse(condition.Value, CultureInfo.InvariantCulture),
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
        public PriorityQueue<FormValues, (string, string)> Examples { get; } = new(
            Comparer<(string, string)>.Create((a, b) => StringComparer.Ordinal.Compare(b.Item1, a.Item1) is var c && c != 0
                ? c : StringComparer.Ordinal.Compare(b.Item2, a.Item2)));
    }

    private sealed class FormValues
    {
        private string[] _dropped = [], _gained = [];
        private string _at = "", _left = "", _right = "";
        private readonly IReadOnlyDictionary<char, string[]> _graphemes;
        private bool _changed;
        private string? _abstract;
        private string Abstract => _abstract ??= StringCorrespondence.AbstractChange(string.Concat(_dropped),
            string.Concat(_gained), _at, Row.Form.EndsWith('-') && !Row.Base.EndsWith('-'));
        public QueryForm Row { get; }
        public FormValues(QueryForm row, IReadOnlyDictionary<char, string[]> graphemes)
        {
            Row = row;
            _graphemes = graphemes;
        }
        private void EnsureChange()
        {
            if (_changed) return;
            _changed = true;
            var a = Tokenize(Row.Base, _graphemes);
            var b = Tokenize(Row.Form, _graphemes);
            var prefix = 0;
            while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) prefix++;
            var suffix = 0;
            while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
            _dropped = a.Skip(prefix).Take(a.Count - prefix - suffix).ToArray();
            _gained = b.Skip(prefix).Take(b.Count - prefix - suffix).ToArray();
            _at = suffix == 0 ? "final" : prefix == 0 ? "initial" : "medial";
            _left = prefix == 0 ? "" : a[prefix - 1];
            _right = suffix == 0 ? "" : a[a.Count - suffix];
        }
        public QueryForm Project()
        {
            EnsureChange();
            return Row with
            {
                Change = new QueryChange(string.Concat(_dropped), string.Concat(_gained), _at, _left, _right, Abstract),
                LettersDropped = _dropped,
                LettersGained = _gained
            };
        }
        public IReadOnlyList<string> Get(string name)
        {
            if (name.StartsWith("change", StringComparison.Ordinal) || name.StartsWith("letters.", StringComparison.Ordinal))
                EnsureChange();
            return name switch
            {
            "id" => [Row.Id], "form" => [Row.Form], "base" => [Row.Base], "kind" => [Row.Kind],
            "entry" => [Row.Entry], "morph-type" => [Row.MorphType], "variant-type" => [Row.VariantType],
            "environments" => Row.Environments, "category" => Row.Category,
            "tokens" => Row.Tokens is { } tokens ? [tokens.ToString(CultureInfo.InvariantCulture)] : [],
            "change" => [string.Concat(_dropped) + ">" + string.Concat(_gained)],
            "change.dropped" => [string.Concat(_dropped)], "change.gained" => [string.Concat(_gained)],
            "change.at" => [_at], "change.left" => [_left], "change.right" => [_right],
            "change.abstract" => [Abstract], "letters.dropped" => _dropped, "letters.gained" => _gained,
                _ => throw new ArgumentException("Unknown query column.")
            };
        }
        private static List<string> Tokenize(string text, IReadOnlyDictionary<char, string[]> graphemes)
        {
            var result = new List<string>();
            for (var i = 0; i < text.Length;)
            {
                string? match = null;
                if (graphemes.TryGetValue(text[i], out var candidates))
                    foreach (var candidate in candidates)
                        if (text.AsSpan(i).StartsWith(candidate, StringComparison.Ordinal)) { match = candidate; break; }
                if (match is null)
                {
                    var rune = Rune.GetRuneAt(text, i);
                    match = rune.ToString();
                }
                result.Add(match);
                i += match.Length;
            }
            return result;
        }
    }
}
