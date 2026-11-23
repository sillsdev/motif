using System.Globalization;
using SIL.Motif.Contract.Query.Corpus;

namespace SIL.Motif.Cli;

/// <summary>
/// The CLI side of <c>motif query --corpus &lt;words&gt; pairs</c>, which needs no project. It is a separate dispatch so the
/// project-based <c>query</c> verb and this one share only the verb name.
/// </summary>
internal static class CorpusQueryCli
{
    private static readonly string[] Flags = ["corpus", "differ", "x", "having", "segments", "fold-case", "max-x", "limit", "examples", "json"];

    /// <summary>True for <c>query --corpus ...</c>, which never reads a project.</summary>
    public static bool Handles(IReadOnlyDictionary<string, string> flags) => flags.ContainsKey("corpus");

    /// <summary>Builds the request from the parsed flags, or false when the invocation is not well formed.</summary>
    public static bool TryBuildRequest(IReadOnlyDictionary<string, string> flags, IReadOnlyList<string> positionals, out PairsQueryRequest request)
    {
        request = null!;
        if (positionals.Count != 1 || positionals[0] != "pairs" || flags.Keys.Any(key => !Flags.Contains(key)) ||
            flags.Any(pair => pair.Value == "true" && pair.Key is not ("json" or "fold-case"))) return false;
        if (!flags.TryGetValue("corpus", out var corpus) || !flags.TryGetValue("differ", out var differ)) return false;
        if (!TryInt("max-x", out var maxX) || !TryInt("limit", out var limit) || !TryInt("examples", out var examples)) return false;
        var having = flags.TryGetValue("having", out var list)
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
        request = new PairsQueryRequest(corpus, differ, flags.GetValueOrDefault("x"), having, flags.GetValueOrDefault("segments"),
            flags.ContainsKey("fold-case"), maxX, limit, examples);
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
