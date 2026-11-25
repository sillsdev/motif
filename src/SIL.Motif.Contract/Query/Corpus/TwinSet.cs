using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Query.Readings;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>One twin of a matching word: the word rewritten by a template, and what the corpus and the parse say about it.</summary>
/// <param name="Label"><c>twin</c> for a lone template, else <c>twin1</c>, <c>twin2</c> in the order given.</param>
/// <param name="Form">The rewritten word.</param>
/// <param name="Tokens">Its tokens in the corpus; 0 when it is not there.</param>
/// <param name="InCorpus">Whether the corpus has the form.</param>
/// <param name="ParseState">Its parse state from the parse index; null when the query read no parse.</param>
/// <param name="Readings">How many readings the grammar gives it; null when the query read no parse.</param>
public sealed record TwinCell(string Label, string Form, long Tokens, bool InCorpus, string? ParseState, int? Readings);

/// <summary>
/// The paradigm twin of <c>--twin</c>: the regex of the query's <c>form ~</c> (or <c>matches</c>) predicate, applied to each word, and one
/// or more templates that rewrite the matched text from the regex's groups (<c>$1</c>, <c>${name}</c>, <c>$0</c>, <c>$$</c>). The first
/// match replaces its span and the rest of the word stays, as a substitution does, so a <c>matches</c> regex rewrites the whole word.
/// The columns <c>twin.form</c>, <c>twin.tokens</c>, <c>twin.inCorpus</c>, <c>twin.parse.state</c> and <c>twin.readings</c> (<c>twin1.form</c> and so on
/// when there are several templates) are the twin's values; a word the regex does not match has none.
/// </summary>
public sealed class TwinSet
{
    private readonly Regex _regex;
    private readonly IReadOnlyList<IReadOnlyList<Part>> _templates;
    private readonly Dictionary<string, TwinCell[]> _cells = new(StringComparer.Ordinal);

    private TwinSet(Regex regex, IReadOnlyList<IReadOnlyList<Part>> templates)
    {
        _regex = regex;
        _templates = templates;
    }

    /// <summary>The number of templates.</summary>
    public int Count => _templates.Count;

    /// <summary>The label of template <paramref name="index"/> (from 0).</summary>
    public string Label(int index) => _templates.Count == 1 ? "twin" : "twin" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private abstract record Part;

    private sealed record Literal(string Text) : Part;

    private sealed record Group(string Name) : Part;

    /// <summary>The column parts of a twin: <c>form</c>, <c>tokens</c>, <c>inCorpus</c>, <c>parse.state</c> and <c>readings</c>.</summary>
    public static readonly IReadOnlyList<string> ColumnParts = ["form", "tokens", "inCorpus", "parse.state", "readings"];

    /// <summary>Whether a column name is a twin column, such as <c>twin.form</c> or <c>twin2.parse.state</c>.</summary>
    public static bool IsColumn(string column) => TryColumn(column, out _, out _);

    /// <summary>Whether a column of the twin holds a number.</summary>
    public static bool IsNumeric(string column) => TryColumn(column, out _, out var part) && part is "tokens" or "readings";

    /// <summary>Reads a twin column into its template number (from 0; <c>twin</c> is the first) and part.</summary>
    public static bool TryColumn(string column, out int index, out string part)
    {
        index = 0;
        part = string.Empty;
        if (!column.StartsWith("twin", StringComparison.Ordinal)) return false;
        var dot = column.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return false;
        var digits = column.AsSpan(4, dot - 4);
        if (digits.Length > 0)
        {
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || digits[0] == '0') return false;
            index = number - 1;
        }
        part = column[(dot + 1)..];
        return ColumnParts.Contains(part);
    }

    /// <summary>Whether a twin column needs the parse of the twin's form.</summary>
    public static bool NeedsParse(string column) => TryColumn(column, out _, out var part) && part is "parse.state" or "readings";

    /// <summary>The highest template number a twin column names (from 1); 0 for none.</summary>
    public static int Template(string column) => TryColumn(column, out var index, out _) ? index + 1 : 0;

    /// <summary>Compiles the templates against the regex of the first positive <c>form ~</c> or <c>form matches</c> of <paramref name="where"/>.</summary>
    /// <exception cref="QueryRefusalException">There is no such regex, a template is malformed or names a group the regex lacks, or the regex is not valid.</exception>
    public static TwinSet Compile(QueryExpr? where, IReadOnlyList<string> templates)
    {
        var leaf = FormRegex(where, true) ?? throw Refuse(RefusalCodes.QueryTwinNeedsRegex,
            "--twin rewrites the words a `form ~ \"regex\"` (or `form matches`) predicate matches, so the where must have one that is not under `not`.");
        var pattern = (string)leaf.Value;
        if (pattern.Length > FormQuery.MaximumRegexLength) throw new ArgumentException("Regular expression is too long.");
        Regex regex;
        try
        {
            _ = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
            regex = new Regex(leaf.Op == QueryOps.Regex ? @"\A(?:" + pattern + @")\z" : pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { throw new ArgumentException("Invalid non-backtracking regular expression.", exception); }
        var parsed = new List<IReadOnlyList<Part>>();
        foreach (var template in templates)
        {
            var parts = Parse(template);
            foreach (var group in parts.OfType<Group>())
                if (!Exists(regex, group.Name))
                    throw Refuse(RefusalCodes.QueryTwinGroupUnknown,
                        $"The --twin template '{template}' names group '{group.Name}', which the regex '{pattern}' does not have (it has {Describe(regex)}).");
            parsed.Add(parts);
        }
        return new TwinSet(regex, parsed);
    }

    /// <summary>Whether the where has a twin column anywhere, or a twin column is asked for by a group.</summary>
    public static bool UsesColumns(QueryExpr? where, IEnumerable<string>? groups) =>
        QueryExprTools.Leaves(where).Any(leaf => IsColumn(leaf.Field)) || (groups?.Any(IsColumn) ?? false);

    private static QueryField? FormRegex(QueryExpr? expr, bool positive) => expr switch
    {
        QueryField { Field: "form", Op: QueryOps.Regex or QueryOps.Contains, Value: string } field when positive => field,
        QueryAnd and => and.Items.Select(item => FormRegex(item, positive)).FirstOrDefault(found => found is not null),
        QueryOr or => or.Items.Select(item => FormRegex(item, positive)).FirstOrDefault(found => found is not null),
        QueryNot not => FormRegex(not.Item, !positive),
        _ => null,
    };

    private static bool Exists(Regex regex, string name) =>
        int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? regex.GetGroupNumbers().Contains(number) : regex.GroupNumberFromName(name) >= 0;

    private static string Describe(Regex regex)
    {
        var names = regex.GetGroupNames().Where(name => name != "0").ToList();
        return names.Count == 0 ? "no groups" : "group" + (names.Count == 1 ? " " : "s ") + string.Join(", ", names);
    }

    private static List<Part> Parse(string template)
    {
        var parts = new List<Part>();
        var literal = new StringBuilder();
        void Flush()
        {
            if (literal.Length > 0) parts.Add(new Literal(literal.ToString()));
            literal.Clear();
        }
        for (var at = 0; at < template.Length; at++)
        {
            if (template[at] != '$') { literal.Append(template[at]); continue; }
            if (at + 1 < template.Length && template[at + 1] == '$') { literal.Append('$'); at++; continue; }
            if (at + 1 < template.Length && template[at + 1] == '{')
            {
                var close = template.IndexOf('}', at + 2);
                if (close < 0 || close == at + 2) throw Refuse(RefusalCodes.QueryTwinGroupUnknown, $"The --twin template '{template}' has an unfinished ${{name}}.");
                Flush();
                parts.Add(new Group(template[(at + 2)..close]));
                at = close;
                continue;
            }
            var end = at + 1;
            while (end < template.Length && template[end] is >= '0' and <= '9') end++;
            if (end == at + 1)
                throw Refuse(RefusalCodes.QueryTwinGroupUnknown, $"The --twin template '{template}' has a '$' that names no group; write $$ for a literal dollar sign.");
            Flush();
            parts.Add(new Group(template[(at + 1)..end]));
            at = end - 1;
        }
        Flush();
        return parts;
    }

    private static QueryRefusalException Refuse(string code, string message) => new(new Refusal(code, FailureReason.InvalidArgument, message));

    /// <summary>The rewrite of a word by each template, null where the regex does not match it.</summary>
    public IReadOnlyList<string>? Forms(string word)
    {
        var match = _regex.Match(word);
        if (!match.Success) return null;
        return [.. _templates.Select(parts =>
        {
            var builder = new StringBuilder(word[..match.Index]);
            foreach (var part in parts)
                builder.Append(part is Literal literal ? literal.Text : match.Groups[((Group)part).Name].Value);
            builder.Append(word[(match.Index + match.Length)..]);
            return builder.ToString();
        })];
    }

    /// <summary>The twin forms that are not words of the corpus, once each, in order.</summary>
    public IReadOnlyList<string> AbsentForms(CorpusWordList corpus) =>
        [.. corpus.Words.SelectMany(word => Forms(word.Form) ?? []).Where(form => !corpus.TryGet(form, out _)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Fills in each matching word's twins; <paramref name="parse"/> answers for corpus and absent forms alike, or is null when no parse is read.</summary>
    public void Bind(CorpusWordList corpus, Func<string, WordReadings?>? parse)
    {
        _cells.Clear();
        foreach (var word in corpus.Words)
        {
            if (Forms(word.Form) is not { } forms) continue;
            _cells[word.Form] = [.. forms.Select((form, index) =>
            {
                var inCorpus = corpus.TryGet(form, out var twin);
                var read = parse?.Invoke(form);
                return new TwinCell(Label(index), form, inCorpus ? twin.Tokens : 0, inCorpus,
                    parse is null ? null : read?.State ?? ReadingStates.Failed, parse is null ? null : read?.Readings.Count);
            })];
        }
    }

    /// <summary>The twins of a word; null when it has none.</summary>
    public IReadOnlyList<TwinCell>? Cells(string word) => _cells.GetValueOrDefault(word);

    /// <summary>The value of a twin column for a word; no value is an empty list.</summary>
    public IReadOnlyList<string> Value(string word, string column)
    {
        if (!TryColumn(column, out var index, out var part) || !_cells.TryGetValue(word, out var cells) || index >= cells.Length) return [];
        var cell = cells[index];
        return part switch
        {
            "form" => [cell.Form],
            "tokens" => [cell.Tokens.ToString(CultureInfo.InvariantCulture)],
            "inCorpus" => [cell.InCorpus ? "true" : "false"],
            "parse.state" => cell.ParseState is null ? [] : [cell.ParseState],
            _ => cell.Readings is null ? [] : [cell.Readings.Value.ToString(CultureInfo.InvariantCulture)],
        };
    }
}
