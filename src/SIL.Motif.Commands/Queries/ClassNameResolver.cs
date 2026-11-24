using System.Text.RegularExpressions;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Lets a pattern name a natural class by its full name as well as its abbreviation. PanGloss knows the abbreviation; when it
/// answers a pattern with <c>unknown_natural_class</c>, the grammar's classes are read once and each bracketed name that matches a
/// class name (case-insensitively, exactly) is replaced by that class's abbreviation and the pattern is sent again. A name two
/// classes share ends the query with both listed; a name that matches nothing ends it with the closest abbreviations and names.
/// Reading the classes only after that answer keeps every pattern that already works free of the read.
/// </summary>
internal sealed partial class ClassNameResolver(IPhonMatcher inner, Func<IReadOnlyList<PhonValue>?> readClasses) : IPhonMatcher
{
    private const int MostSuggestions = 5;

    private IReadOnlyList<PhonValue>? _classes;
    private bool _read;
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    public string? ParserVersion => inner.ParserVersion;

    public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var text = _resolved.GetValueOrDefault(patternText, patternText);
        try
        {
            return inner.Match(text, words, cancellationToken);
        }
        catch (QueryRefusalException refused) when (IsUnknownClass(refused) && Classes() is { Count: > 0 } classes)
        {
            var rewritten = Rewrite(text, classes);
            if (rewritten == text) throw WithSuggestions(refused, text, classes);
            _resolved[patternText] = rewritten;
            try
            {
                return inner.Match(rewritten, words, cancellationToken);
            }
            catch (QueryRefusalException again) when (IsUnknownClass(again))
            {
                throw WithSuggestions(again, rewritten, classes);
            }
        }
    }

    private static bool IsUnknownClass(QueryRefusalException refused) =>
        refused.Refusal.Facts is { } facts && facts.TryGetValue("panglossCode", out var code) && code == "unknown_natural_class";

    private IReadOnlyList<PhonValue>? Classes()
    {
        if (!_read)
        {
            _read = true;
            _classes = readClasses();
        }
        return _classes;
    }

    /// <summary>The pattern with each bracketed class name that is not an abbreviation but is one class's name replaced by that abbreviation.</summary>
    internal static string Rewrite(string pattern, IReadOnlyList<PhonValue> classes) =>
        BracketPattern().Replace(pattern, match =>
        {
            var name = match.Groups[1].Value;
            if (IsBundle(name) || classes.Any(value => value.Abbreviation == name)) return match.Value;
            var named = classes.Where(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count > 1)
                throw QueryRefusalException.PhonPatternRefused(
                    $"The class name \"{name}\" names {named.Count} classes: {string.Join(", ", named.Select(Describe))}. Write the abbreviation of the one you mean.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = name, ["classes"] = string.Join(", ", named.Select(Describe)) });
            return named.Count == 1 && named[0].Abbreviation is { Length: > 0 } abbreviation ? "[" + abbreviation + "]" : match.Value;
        });

    private static QueryRefusalException WithSuggestions(QueryRefusalException refused, string pattern, IReadOnlyList<PhonValue> classes)
    {
        var unknown = BracketPattern().Matches(pattern).Select(match => match.Groups[1].Value)
            .Where(name => !IsBundle(name) && !classes.Any(value => value.Abbreviation == name || (value.Abbreviation is null && value.Name == name)))
            .Distinct(StringComparer.Ordinal).ToList();
        var facts = new Dictionary<string, string>(refused.Refusal.Facts ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        var advice = new List<string>();
        foreach (var name in unknown)
        {
            var closest = Closest(name, classes);
            if (closest.Count == 0) continue;
            advice.Add($"\"{name}\" is not a class here; closest: {string.Join(", ", closest.Select(Describe))}.");
            facts["suggestions"] = string.Join(", ", closest.Select(value => value.Token ?? value.Name));
        }
        if (advice.Count == 0) return refused;
        return QueryRefusalException.PhonPatternRefused(
            refused.Refusal.Message + " " + string.Join(" ", advice) + " Run motif query --values classes to list them.", facts);
    }

    private static string Describe(PhonValue value) =>
        value.Abbreviation is { Length: > 0 } abbreviation && abbreviation != value.Name ? $"{abbreviation} ({value.Name})" : value.Name;

    /// <summary>At most five classes whose abbreviation or name begins with, contains, or is a few edits from the name, best first.</summary>
    internal static IReadOnlyList<PhonValue> Closest(string name, IReadOnlyList<PhonValue> classes)
    {
        var wanted = name.ToLowerInvariant();
        var allowed = Math.Max(2, wanted.Length / 3);
        return classes.Select(value =>
            {
                var best = int.MaxValue;
                foreach (var candidate in new[] { value.Abbreviation, value.Name })
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    var other = candidate.ToLowerInvariant();
                    var rank = other.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(other, StringComparison.Ordinal) ? 0
                        : other.Contains(wanted, StringComparison.Ordinal) || wanted.Contains(other, StringComparison.Ordinal) ? 1
                        : Distance(wanted, other) is var distance && distance <= allowed ? 1 + distance : int.MaxValue;
                    best = Math.Min(best, rank);
                }
                return (value, best);
            })
            .Where(pair => pair.best != int.MaxValue)
            .OrderBy(pair => pair.best).ThenBy(pair => pair.value.Name.Length).ThenBy(pair => pair.value.Name, StringComparer.Ordinal)
            .Take(MostSuggestions).Select(pair => pair.value).ToList();
    }

    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[b.Length];
    }

    private static bool IsBundle(string inner) => inner.Length == 0 || inner[0] is '+' or '-' || (inner[0] >= 'α' && inner[0] <= 'ω');

    [GeneratedRegex(@"\[([^\]]*)\]")]
    private static partial Regex BracketPattern();
}
