using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Contract.Query;

/// <summary>The named fold sets of the <c>fold</c> column: a key two spellings share when they differ only in a declared way.</summary>
public static class QueryFold
{
    /// <summary>The fold used when a query names none: Unicode NFC, then lower case.</summary>
    public const string Default = "nfc,case";

    /// <summary>The token that the command replaces by the project's <c>[parsimony] fold</c> rules before evaluation.</summary>
    public const string ProjectToken = "project";

    private static readonly Dictionary<string, FoldRule> Named = new(StringComparer.Ordinal)
    {
        ["iy~y"] = new("iy", "y"), ["o~u"] = new("o", "u"), ["e~i"] = new("e", "i"), ["c~k"] = new("c", "k"),
    };

    /// <summary>Parses a fold specification into one function, or throws <see cref="ArgumentException"/>.</summary>
    /// <param name="spec">Comma-separated parts; null or empty means <see cref="Default"/>.</param>
    public static Func<string, string> Compile(string? spec)
    {
        var nfc = false;
        var lower = false;
        var rules = new List<FoldRule>();
        foreach (var part in (string.IsNullOrWhiteSpace(spec) ? Default : spec).Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0) continue;
            if (part == "nfc") nfc = true;
            else if (part == "case") lower = true;
            else if (part == ProjectToken) throw new ArgumentException("The project fold rules are resolved by the command.");
            else if (Named.TryGetValue(part, out var named)) rules.Add(named);
            else if (FoldRule.TryParse(part, out var raw)) rules.Add(raw);
            else throw new ArgumentException($"Unknown fold set '{part}'.");
        }
        return text =>
        {
            if (nfc) text = text.Normalize(NormalizationForm.FormC);
            if (lower) text = text.ToLower(CultureInfo.InvariantCulture);
            foreach (var rule in rules) text = text.Replace(rule.From, rule.To, StringComparison.Ordinal);
            return text;
        };
    }

    /// <summary>Replaces the <see cref="ProjectToken"/> part of a specification by the given project rules.</summary>
    public static string? ResolveProject(string? spec, IReadOnlyList<FoldRule> projectRules)
    {
        if (spec is null) return null;
        var parts = spec.Split(',', StringSplitOptions.TrimEntries).SelectMany(part => part == ProjectToken
            ? projectRules.Select(rule => rule.ToString()) : [part]);
        return string.Join(',', parts.Where(part => part.Length > 0));
    }
}
