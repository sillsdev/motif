using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query;

/// <summary>What a <c>motif query</c> command line asks for, beyond the <see cref="QueryRecord"/> that selects the rows.</summary>
/// <param name="Record">The query.</param>
/// <param name="Project">The <c>--project</c> value, null when the line has none.</param>
/// <param name="AsOf">The <c>--as-of</c> revision, null when the line has none.</param>
/// <param name="Json">Whether <c>--json</c> was given.</param>
/// <param name="TestResult">Whether <c>--test-result</c> was given.</param>
/// <param name="MembersFile">The <c>--members</c> path; the caller reads it, the parser does not touch files.</param>
public sealed record QueryArguments(QueryRecord Record, string? Project, string? AsOf, bool Json, bool TestResult,
    string? MembersFile);

/// <summary>The flags of one <c>motif group suffix|prefix</c> command line.</summary>
public sealed record GroupRunArguments(string Kind, string? Ending, string? Start, string? Pattern, string? ParseState,
    int? Limit, int? Page, string? AsOf, string? Fold, int? MinStem);

/// <summary>One parsed run line: exactly one of <see cref="Query"/> and <see cref="Group"/> is set.</summary>
public sealed record RunLine(string Verb, QueryArguments? Query, GroupRunArguments? Group)
{
    /// <summary>The revision the line reads as of, whichever verb it is.</summary>
    public string? AsOf => Query?.AsOf ?? Group?.AsOf;
}

/// <summary>Turns one <c>motif query</c> or <c>motif group</c> line into typed arguments; any other verb is a refusal.</summary>
public static class QueryRunLine
{
    public const string QueryVerb = "query";
    public const string GroupVerb = "group";

    private static readonly string[] QueryFlags =
    [
        "project", "where", "group", "min-count", "examples", "ids", "raw-letters", "json", "trial", "fold", "distinct",
        "change", "at", "affixes", "wordset", "discovery", "heldout", "score", "exception", "unit", "controls",
        "control-letters", "test-result", "members", "context", "sides", "sort",
    ];

    private static readonly string[] SavedFlags = ["project", "examples", "json", "trial"];

    private static readonly string[] GroupFlags =
        ["ending", "start", "pattern", "parse-state", "limit", "page", "fold", "as-of", "min-stem"];

    private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "ids", "raw-letters", "json", "test-result" };

    /// <summary>Parses a run line that is a <c>motif query</c>; a <c>motif group</c> line is refused here, see <see cref="TryParseRun"/>.</summary>
    public static bool TryParse(string run, [NotNullWhen(true)] out QueryRecord? record, [NotNullWhen(false)] out Refusal? refusal)
    {
        record = null;
        if (!TryParseRun(run, out var line, out refusal)) return false;
        if (line.Query is null)
        {
            refusal = Invalid("A group line returns word groups, not a query record; use TryParseRun.", ("verb", line.Verb));
            return false;
        }
        record = line.Query.Record;
        return true;
    }

    /// <summary>Parses one run line of a Guide query: the verb must be <c>motif query</c> or <c>motif group</c>, with no <c>--project</c>.</summary>
    public static bool TryParseRun(string run, [NotNullWhen(true)] out RunLine? line, [NotNullWhen(false)] out Refusal? refusal)
    {
        line = null;
        if (!TryTokenize(run, out var tokens, out refusal)) return false;
        if (tokens.Count == 0 || tokens[0] != "motif" || tokens.Count < 2)
        {
            refusal = Invalid("A run line is one command that starts with motif and a verb.", ("run", run));
            return false;
        }
        var verb = tokens[1];
        var args = tokens.Skip(2).ToArray();
        switch (verb)
        {
            case QueryVerb:
                if (!TryParseQueryArguments(args, true, out var query, out refusal)) return false;
                if (query.Project is not null || query.MembersFile is not null)
                {
                    refusal = Invalid("A run line leaves out --project and --members; the renderer supplies the project.",
                        ("flag", query.Project is not null ? "project" : "members"));
                    return false;
                }
                line = new RunLine(verb, query, null);
                return true;
            case GroupVerb:
                if (!TryParseGroupArguments(args, out var group, out refusal)) return false;
                line = new RunLine(verb, null, group);
                return true;
            default:
                refusal = new Refusal(RefusalCodes.GuideQueryRunNotReadOnly, FailureReason.Refused,
                    "Only motif query and motif group may run from a Guide.", Facts(("verb", verb)));
                return false;
        }
    }

    /// <summary>Parses the tokens after <c>motif query</c>; the CLI and a run line share this, so they accept the same lines.</summary>
    /// <param name="tokens">The arguments after <c>query</c>.</param>
    /// <param name="allowAsOf">Whether <c>--as-of</c> is a flag of this line.</param>
    /// <param name="parsed">The arguments when the line parses.</param>
    /// <param name="refusal">Why it does not, otherwise.</param>
    public static bool TryParseQueryArguments(IReadOnlyList<string> tokens, bool allowAsOf, [NotNullWhen(true)] out QueryArguments? parsed,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        parsed = null;
        if (!TryReadFlags(tokens, out var flags, out var positionals, out refusal)) return false;
        return TryBuildQuery(flags, positionals, tokens, allowAsOf, out parsed, out refusal);
    }

    /// <summary>Builds the query from flags and positionals a caller already split, so the CLI keeps its own argument grammar.</summary>
    /// <param name="flags">Each flag's last value; a switch holds any text.</param>
    /// <param name="positionals">The words that are not flags, after <c>query</c>.</param>
    /// <param name="tokens">The raw words, scanned for the repeatable <c>--where</c>, <c>--exception</c> and <c>--context</c>.</param>
    /// <param name="allowAsOf">Whether <c>--as-of</c> is a flag of this line.</param>
    /// <param name="parsed">The arguments when the line parses.</param>
    /// <param name="refusal">Why it does not, otherwise.</param>
    public static bool TryBuildQuery(IReadOnlyDictionary<string, string> flags, IReadOnlyList<string> positionals,
        IReadOnlyList<string> tokens, bool allowAsOf, [NotNullWhen(true)] out QueryArguments? parsed, [NotNullWhen(false)] out Refusal? refusal)
    {
        parsed = null;
        if (!TryRepeated(tokens, "where", out var wheres, out refusal) || !TryRepeated(tokens, "exception", out var exceptions, out refusal) ||
            !TryRepeated(tokens, "context", out var contexts, out refusal))
            return false;
        var saved = positionals.Count == 2 && positionals[0] == "saved";
        var evidence = positionals.Count == 2 && positionals[0] == "evidence" && positionals[1] is "stems" or "forms";
        var source = evidence ? positionals[1] : positionals.FirstOrDefault();
        if (!(saved || evidence || positionals.Count == 1 && QuerySources.All.Contains(positionals[0])))
        {
            refusal = Invalid("A query names a source (forms, stems, uses, objects), saved <id> or evidence <stems|forms>.",
                ("positionals", string.Join(" ", positionals)));
            return false;
        }
        var allowed = saved ? SavedFlags : QueryFlags;
        foreach (var name in flags.Keys)
            if (!allowed.Contains(name) && !(allowAsOf && name == "as-of"))
            {
                refusal = Invalid("This query does not take that flag.", ("flag", name));
                return false;
            }
        if (!TryCount(flags, "min-count", "1", 1, int.MaxValue, out var minCount, out refusal) ||
            !TryCount(flags, "examples", "5", 0, 1000, out var examples, out refusal))
            return false;
        QueryRecord record;
        if (saved)
        {
            if (!SavedQueries.TryGet(positionals[1], out var shipped))
            {
                refusal = new Refusal(RefusalCodes.GuideQueryRunInvalid, FailureReason.InvalidArgument,
                    "No saved query has that id.", Facts(("id", positionals[1])));
                return false;
            }
            record = shipped with { Examples = examples, TrialId = flags.GetValueOrDefault("trial") };
        }
        else
        {
            var selectors = new List<QueryCondition>();
            var exceptionList = new List<QueryCondition>();
            var contextList = new List<QueryCondition>();
            foreach (var (values, target, name) in new[] { (wheres, selectors, "where"), (exceptions, exceptionList, "exception"), (contexts, contextList, "context") })
                foreach (var text in values)
                    try { target.Add(QueryConditionParser.Parse(text)); }
                    catch (ArgumentException)
                    {
                        refusal = Invalid("A selector needs a column, operator and value.", ("flag", name), ("value", text));
                        return false;
                    }
            QueryHypothesis? hypothesis = null;
            if (flags.TryGetValue("change", out var changeText))
            {
                var arrow = changeText.IndexOf('>');
                if (arrow < 1 || !int.TryParse(flags.GetValueOrDefault("controls", "5"), out var controls))
                {
                    refusal = Invalid("--change is <from>><to> and --controls is a number.", ("value", changeText));
                    return false;
                }
                hypothesis = new QueryHypothesis(changeText[..arrow], changeText[(arrow + 1)..], flags.GetValueOrDefault("at", "final"),
                    List(flags, "affixes"), List(flags, "wordset"), List(flags, "discovery"), List(flags, "heldout"), controls,
                    flags.ContainsKey("control-letters") ? List(flags, "control-letters") : null);
            }
            else if (new[] { "at", "affixes", "wordset", "discovery", "heldout", "controls", "control-letters" }.Any(flags.ContainsKey))
            {
                refusal = Invalid("--at, --affixes, --wordset, --discovery, --heldout, --controls and --control-letters need --change.");
                return false;
            }
            record = new QueryRecord(source!, selectors, List(flags, "group"), minCount, examples, flags.ContainsKey("ids"),
                flags.ContainsKey("raw-letters"), flags.GetValueOrDefault("trial"), flags.GetValueOrDefault("fold"),
                flags.GetValueOrDefault("distinct"), hypothesis, flags.ContainsKey("score") ? List(flags, "score") : null,
                exceptionList.Count == 0 ? null : exceptionList, flags.GetValueOrDefault("unit"), null,
                contextList.Count == 0 ? null : contextList, evidence, flags.GetValueOrDefault("as-of"),
                flags.GetValueOrDefault("sides"), flags.GetValueOrDefault("sort"));
        }
        parsed = new QueryArguments(record, flags.GetValueOrDefault("project"), flags.GetValueOrDefault("as-of"),
            flags.ContainsKey("json"), flags.ContainsKey("test-result"), flags.GetValueOrDefault("members"));
        return true;
    }

    /// <summary>Splits a run line into words with shell-style quotes, refusing a line break and anything that would chain, redirect or substitute outside quotes.</summary>
    public static bool TryTokenize(string run, [NotNullWhen(true)] out IReadOnlyList<string>? tokens, [NotNullWhen(false)] out Refusal? refusal)
    {
        tokens = null;
        var list = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        for (var i = 0; i < run.Length; i++)
        {
            var c = run[i];
            if (c is '\'' or '"')
            {
                var end = i + 1;
                inWord = true;
                while (true)
                {
                    if (end >= run.Length)
                    {
                        refusal = Invalid("A quote in the run line is never closed.", ("run", run));
                        return false;
                    }
                    var q = run[end];
                    if (q == c) break;
                    if (c == '"' && q == '\\' && end + 1 < run.Length && run[end + 1] is '"' or '\\') q = run[++end];
                    word.Append(q);
                    end++;
                }
                i = end;
            }
            else if (c is ';' or '|' or '&' or '>' or '<' or '`' or '$' or '\n' or '\r')
            {
                refusal = new Refusal(RefusalCodes.GuideQueryRunNotReadOnly, FailureReason.Refused,
                    "A run line is exactly one command, with no chaining, redirection or substitution; quote a value that contains > or <.",
                    Facts(("character", c.ToString())));
                return false;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord) list.Add(word.ToString());
                word.Clear();
                inWord = false;
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }
        if (inWord) list.Add(word.ToString());
        tokens = list;
        refusal = null;
        return true;
    }

    private static bool TryParseGroupArguments(IReadOnlyList<string> tokens, [NotNullWhen(true)] out GroupRunArguments? parsed,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        parsed = null;
        if (!TryReadFlags(tokens, out var flags, out var positionals, out refusal)) return false;
        if (positionals.Count != 1 || positionals[0] is not ("suffix" or "prefix"))
        {
            refusal = Invalid("motif group takes suffix or prefix.", ("positionals", string.Join(" ", positionals)));
            return false;
        }
        foreach (var name in flags.Keys)
            if (!GroupFlags.Contains(name))
            {
                refusal = Invalid("motif group does not take that flag in a run line.", ("flag", name));
                return false;
            }
        if (!TryOptionalCount(flags, "limit", out var limit, out refusal) || !TryOptionalCount(flags, "page", out var page, out refusal) ||
            !TryOptionalCount(flags, "min-stem", out var minStem, out refusal))
            return false;
        parsed = new GroupRunArguments(positionals[0], flags.GetValueOrDefault("ending"), flags.GetValueOrDefault("start"),
            flags.GetValueOrDefault("pattern"), flags.GetValueOrDefault("parse-state"), limit, page, flags.GetValueOrDefault("as-of"),
            flags.GetValueOrDefault("fold"), minStem);
        return true;
    }

    private static bool TryRepeated(IReadOnlyList<string> tokens, string name, out List<string> values, [NotNullWhen(false)] out Refusal? refusal)
    {
        values = [];
        refusal = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] != "--" + name) continue;
            if (++i >= tokens.Count || tokens[i].StartsWith("--", StringComparison.Ordinal))
            {
                refusal = Invalid("A flag needs a value.", ("flag", name));
                return false;
            }
            values.Add(tokens[i]);
        }
        return true;
    }

    private static bool TryReadFlags(IReadOnlyList<string> tokens, out Dictionary<string, string> flags, out List<string> positionals,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        flags = new Dictionary<string, string>(StringComparer.Ordinal);
        positionals = [];
        refusal = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }
            var name = token[2..];
            if (Switches.Contains(name))
            {
                flags[name] = "true";
                continue;
            }
            if (i + 1 >= tokens.Count || tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                refusal = Invalid("A flag needs a value.", ("flag", name));
                return false;
            }
            flags[name] = tokens[++i];
        }
        return true;
    }

    private static bool TryCount(IReadOnlyDictionary<string, string> flags, string name, string fallback, int min, int max, out int value,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        refusal = null;
        if (int.TryParse(flags.GetValueOrDefault(name, fallback), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value >= min && value <= max) return true;
        refusal = Invalid($"--{name} is a whole number from {min} to {max}.", ("flag", name));
        return false;
    }

    private static bool TryOptionalCount(IReadOnlyDictionary<string, string> flags, string name, out int? value, [NotNullWhen(false)] out Refusal? refusal)
    {
        value = null;
        refusal = null;
        if (!flags.TryGetValue(name, out var text)) return true;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1)
        {
            value = number;
            return true;
        }
        refusal = Invalid($"--{name} is a positive whole number.", ("flag", name));
        return false;
    }

    private static string[] List(IReadOnlyDictionary<string, string> flags, string name) => flags.TryGetValue(name, out var list)
        ? list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    private static Refusal Invalid(string message, params (string Key, string Value)[] facts) =>
        new(RefusalCodes.GuideQueryRunInvalid, FailureReason.InvalidArgument, message, Facts(facts));

    private static Dictionary<string, string> Facts(params (string Key, string Value)[] facts) =>
        facts.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
}
