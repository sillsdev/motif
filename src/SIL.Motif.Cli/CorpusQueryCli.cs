using System.Globalization;
using SIL.Motif.Contract.Query.Corpus;

namespace SIL.Motif.Cli;

/// <summary>
/// The CLI side of <c>motif query --corpus &lt;words&gt; pairs</c>, which needs no project. It is a separate dispatch so the
/// project-based <c>query</c> verb and this one share only the verb name.
/// </summary>
internal static class CorpusQueryCli
{
    private static readonly string[] Flags = ["corpus", "differ", "x", "having", "segments", "fold-case", "split-hyphens", "max-x", "limit", "examples", "json"];

    /// <summary>True for <c>query --corpus ...</c>, which never reads a project.</summary>
    public static bool Handles(IReadOnlyDictionary<string, string> flags) => flags.ContainsKey("corpus");

    private static readonly string[] FormsFlags = ["corpus", "where", "query-json", "grammar", "segments", "fold-case", "split-hyphens", "limit", "project", "thorough", "segment-test", "group-by", "what-if-retire", "sum", "sample", "seed", "write-words", "with-counts", "force", "json"];

    /// <summary>True for <c>query --corpus &lt;words&gt; forms ...</c>.</summary>
    public static bool IsForms(IReadOnlyList<string> positionals) => positionals.FirstOrDefault() == "forms";

    /// <summary>
    /// Builds a corpus forms request from <c>forms where &lt;expression&gt;</c> or <c>forms --where &lt;expression&gt;</c> or
    /// <c>forms --query-json &lt;file|-&gt;</c>; <paramref name="queryJson"/> is read by the caller from the named source.
    /// </summary>
    public static bool TryBuildFormsRequest(IReadOnlyDictionary<string, string> flags, IReadOnlyList<string> positionals,
        Func<string, string?> readJson, out CorpusFormsRequest request)
    {
        request = null!;
        if (flags.Keys.Any(key => !FormsFlags.Contains(key)) ||
            flags.Any(pair => pair.Value == "true" && pair.Key is not ("json" or "fold-case" or "split-hyphens" or "thorough" or "with-counts" or "force"))) return false;
        string? where = flags.GetValueOrDefault("where");
        if (positionals is ["forms", "where", var text]) where = where is null ? text : null;
        else if (positionals is not ["forms"]) return false;
        var groupBy = flags.GetValueOrDefault("group-by");
        var retire = flags.TryGetValue("what-if-retire", out var retireText)
            ? retireText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
        if (retire is { Length: 0 }) return false;
        string? document = null;
        if (flags.TryGetValue("query-json", out var source))
        {
            document = readJson(source);
            if (document is null) return false;
        }
        var readingsOnly = groupBy is not null || retire is not null || flags.ContainsKey("sum") || flags.ContainsKey("sample") || flags.ContainsKey("write-words");
        if (!flags.TryGetValue("corpus", out var corpus) || ((where is null) == (document is null) && !(readingsOnly && where is null && document is null))) return false;
        int? limit = null;
        if (flags.TryGetValue("limit", out var raw))
        {
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return false;
            limit = parsed;
        }
        int? sample = null;
        if (flags.TryGetValue("sample", out var rawSample))
        {
            if (!int.TryParse(rawSample, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSample)) return false;
            sample = parsedSample;
        }
        long? seed = null;
        if (flags.TryGetValue("seed", out var rawSeed))
        {
            if (!long.TryParse(rawSeed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedSeed)) return false;
            seed = parsedSeed;
        }
        request = new CorpusFormsRequest(corpus, where, document, flags.GetValueOrDefault("grammar"), flags.GetValueOrDefault("segments"),
            flags.ContainsKey("fold-case"), flags.ContainsKey("split-hyphens"), limit, flags.GetValueOrDefault("segment-test"), flags.GetValueOrDefault("project"), groupBy, retire, flags.ContainsKey("thorough"),
            flags.GetValueOrDefault("sum"), sample, seed, flags.GetValueOrDefault("write-words"), flags.ContainsKey("with-counts"), flags.TryGetValue("force", out var force) && force != "false");
        return true;
    }

    /// <summary>Builds the request from the parsed flags, or false when the invocation is not well formed.</summary>
    public static bool TryBuildRequest(IReadOnlyDictionary<string, string> flags, IReadOnlyList<string> positionals, out PairsQueryRequest request)
    {
        request = null!;
        if (positionals.Count != 1 || positionals[0] != "pairs" || flags.Keys.Any(key => !Flags.Contains(key)) ||
            flags.Any(pair => pair.Value == "true" && pair.Key is not ("json" or "fold-case" or "split-hyphens"))) return false;
        if (!flags.TryGetValue("corpus", out var corpus) || !flags.TryGetValue("differ", out var differ)) return false;
        if (!TryInt("max-x", out var maxX) || !TryInt("limit", out var limit) || !TryInt("examples", out var examples)) return false;
        var having = flags.TryGetValue("having", out var list)
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
        request = new PairsQueryRequest(corpus, differ, flags.GetValueOrDefault("x"), having, flags.GetValueOrDefault("segments"),
            flags.ContainsKey("fold-case"), flags.ContainsKey("split-hyphens"), maxX, limit, examples);
        return true;

        bool TryInt(string name, out int? value)
        {
            value = null;
            if (!flags.TryGetValue(name, out var raw)) return true;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return false;
            value = parsed;
            return true;
        }
    }
}
