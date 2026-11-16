using System.Globalization;
using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Contract.Query;

/// <summary>A grouping request exceeded its bounded result size.</summary>
public sealed class QueryTooManyGroupsException(string columns) : ArgumentException($"Too many groups for columns: {columns}.");

/// <summary>Evaluates a query over a caller-owned immutable snapshot of one Trial's sources.</summary>
public static partial class FormQuery
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
    private static readonly QuerySpec FormSpec = new(
        c => Columns.Contains(c) || QuerySources.IsTextColumn(c), Sets.Contains, c => QuerySources.IsNumeric(QuerySources.Forms, c));

    /// <summary>Validates the complete record, then returns matching rows or grouped counts.</summary>
    public static QueryResult Run(QueryRecord query, IReadOnlyList<QueryForm> rows, IReadOnlyList<string> graphemes)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(graphemes);
        if (query.From != QuerySources.Forms) throw new ArgumentException("Invalid forms query.");
        RejectHypothesisFields(query, "the forms source");
        var fold = QueryFold.Compile(query.Fold);
        var segmentIndex = Index(query.RawLetters ? [] : graphemes);
        var outcome = QueryEngine.Execute(query, rows.Select(row => (IQueryRowView)new FormValues(row, segmentIndex, fold)), FormSpec);
        return new QueryResult([.. outcome.Selected.Select(v => ((FormValues)v).Project())],
            [.. outcome.Groups.Select(g => new QueryGroup(g.Key, g.Count, g.Tokens,
                [.. g.Examples.Select(v => ((FormValues)v).Project())]))], outcome.Ids);
    }

    /// <summary>Evaluates a query over the stems, uses or objects source.</summary>
    /// <param name="query">The record; its <c>From</c> names the source.</param>
    /// <param name="rows">The stored rows of that source.</param>
    /// <param name="graphemes">The project's grapheme table, used by the text columns of stems.</param>
    public static QueryResult RunRows(QueryRecord query, IReadOnlyList<QueryRow> rows, IReadOnlyList<string> graphemes)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(graphemes);
        var source = query.From;
        if (source is not (QuerySources.Stems or QuerySources.Uses or QuerySources.Objects)) throw new ArgumentException("Invalid query source.");
        RejectHypothesisFields(query, "this query");
        var fold = QueryFold.Compile(query.Fold);
        var segmentIndex = Index(query.RawLetters ? [] : graphemes);
        var stored = QuerySources.StoredColumns(source);
        var spec = new QuerySpec(c => QuerySources.IsColumn(source, c), c => QuerySources.IsStoredSet(source, c),
            c => QuerySources.IsNumeric(source, c));
        var outcome = QueryEngine.Execute(query, rows.Select(row => (IQueryRowView)new StoredValues(source, stored, row, segmentIndex, fold)), spec);
        QueryItem Item(IQueryRowView view) => ((StoredValues)view).Project();
        return new QueryResult([], [.. outcome.Groups.Select(g => new QueryGroup(g.Key, g.Count, g.Tokens, [])
            { Samples = [.. g.Examples.Select(Item)] })], outcome.Ids) { Items = [.. outcome.Selected.Select(Item)] };
    }

    private static IReadOnlyDictionary<char, string[]> Index(IReadOnlyList<string> graphemes) => graphemes
        .Where(g => !string.IsNullOrEmpty(g)).Distinct(StringComparer.Ordinal)
        .OrderByDescending(g => g.Length).ThenBy(g => g, StringComparer.Ordinal)
        .GroupBy(g => g[0]).ToDictionary(g => g.Key, g => g.ToArray());

    private sealed class StoredValues(string source, IReadOnlyList<string> columns, QueryRow row,
        IReadOnlyDictionary<char, string[]> graphemes, Func<string, string> fold) : IQueryRowView
    {
        private TextColumns? _text;
        public string Id => row.Id;
        public string SortKey => Get(QuerySources.SortColumn(source)) is { Count: > 0 } sort ? sort[0] : "";
        public long? Tokens => null;
        public IReadOnlyList<string> Get(string column)
        {
            if (column == "id") return [row.Id];
            var at = IndexOf(column);
            if (at >= 0) return row.Values[at];
            _text ??= new TextColumns(Get("form")[0], graphemes, fold);
            return _text.Get(column);
        }
        private int IndexOf(string column)
        {
            for (var i = 0; i < columns.Count; i++) if (columns[i] == column) return i;
            return -1;
        }
        public QueryItem Project()
        {
            var values = new Dictionary<string, string[]>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++) values[columns[i]] = row.Values[i];
            return new QueryItem(row.Id, values);
        }
    }

    private sealed class FormValues : IQueryRowView
    {
        private string[] _dropped = [], _gained = [];
        private string _at = "", _left = "", _right = "";
        private readonly IReadOnlyDictionary<char, string[]> _graphemes;
        private readonly TextColumns _text;
        private bool _changed;
        private string? _abstract;
        private string Abstract => _abstract ??= StringCorrespondence.AbstractChange(string.Concat(_dropped),
            string.Concat(_gained), _at, Row.Form.EndsWith('-') && !Row.Base.EndsWith('-'));
        public QueryForm Row { get; }
        public string Id => Row.Id;
        public string SortKey => Row.Form;
        public long? Tokens => Row.Tokens;
        public FormValues(QueryForm row, IReadOnlyDictionary<char, string[]> graphemes, Func<string, string> fold)
        {
            Row = row;
            _graphemes = graphemes;
            _text = new TextColumns(row.Form, graphemes, fold);
        }
        private void EnsureChange()
        {
            if (_changed) return;
            _changed = true;
            var a = TextColumns.Tokenize(Row.Base, _graphemes);
            var b = _text.Segments;
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
                _ when QuerySources.IsTextColumn(name) => _text.Get(name),
                _ => throw new ArgumentException("Unknown query column.")
            };
        }
    }
}
