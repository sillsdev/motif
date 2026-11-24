using System.Text.RegularExpressions;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Lets a pattern name a natural class by a different case or by its id as well as by the label PanGloss knows. When PanGloss
/// answers a pattern with <c>unknown_natural_class</c>, the grammar's classes are read once from its <c>inventory</c> op and each
/// bracketed name that matches one class's label, token or id case-insensitively is replaced by that class's token and the pattern is
/// sent again. A name two classes share ends the query with both listed; a name that matches nothing ends it with the closest labels.
/// Text inside <c>/.../</c> literals is never touched. Reading the classes only after that answer keeps every pattern that already
/// works free of the read, and when the read fails or lists nothing the refusal says so.
/// </summary>
internal sealed partial class ClassNameResolver(IPhonMatcher inner) : IPhonMatcher
{
    private const int MostSuggestions = 5;

    private IReadOnlyList<PhonInventoryClass>? _classes;
    private string? _readFailure;
    private bool _read;
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    public string? ParserVersion => inner.ParserVersion;

    public bool MatchesByIdentity => inner.MatchesByIdentity;

    public PhonInventory? Inventory(CancellationToken cancellationToken) => inner.Inventory(cancellationToken);

    public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var text = _resolved.GetValueOrDefault(patternText, patternText);
        try
        {
            return inner.Match(text, words, cancellationToken);
        }
        catch (QueryRefusalException refused) when (IsUnknownClass(refused))
        {
            var classes = Classes(cancellationToken);
            if (classes is not { Count: > 0 })
                throw Annotated(refused, _readFailure ?? "PanGloss lists no natural classes for this grammar, so Motif cannot suggest a name.");
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

    private IReadOnlyList<PhonInventoryClass>? Classes(CancellationToken cancellationToken)
    {
        if (_read) return _classes;
        _read = true;
        try
        {
            _classes = inner.Inventory(cancellationToken)?.Classes;
            if (_classes is null) _readFailure = "PanGloss could not list the grammar's natural classes, so Motif cannot suggest a name.";
        }
        catch (QueryRefusalException failed)
        {
            _readFailure = "Motif could not read the grammar's natural classes to suggest a name: " + failed.Refusal.Message;
        }
        return _classes;
    }

    private static QueryRefusalException Annotated(QueryRefusalException refused, string note) =>
        QueryRefusalException.PhonPatternRefused(refused.Refusal.Message + " " + note,
            refused.Refusal.Facts ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static bool IsKnown(string name, IReadOnlyList<PhonInventoryClass> classes) =>
        classes.Any(value => value.Token == name || value.Name == name || value.Id == name);

    /// <summary>The pattern with each bracketed name outside a literal that is no class's token, label or id but is one class's, ignoring case, replaced by that class's token.</summary>
    internal static string Rewrite(string pattern, IReadOnlyList<PhonInventoryClass> classes) =>
        Brackets().Replace(pattern, match =>
        {
            if (match.Groups["literal"].Success) return match.Value;
            var name = match.Groups["name"].Value;
            if (IsBundle(name) || IsKnown(name, classes)) return match.Value;
            var named = classes.Where(value => new[] { value.Name, value.Token, value.Id }.Any(text => string.Equals(text, name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (named.Count > 1)
                throw QueryRefusalException.PhonPatternRefused(
                    $"The class name \"{name}\" names {named.Count} classes: {string.Join(", ", named.Select(Describe))}. Write the token of the one you mean.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = name, ["classes"] = string.Join(", ", named.Select(Describe)) });
            return named.Count == 1 && named[0].Token is { } token ? "[" + token + "]" : match.Value;
        });

    private static QueryRefusalException WithSuggestions(QueryRefusalException refused, string pattern, IReadOnlyList<PhonInventoryClass> classes)
    {
        var unknown = Brackets().Matches(pattern).Where(match => !match.Groups["literal"].Success).Select(match => match.Groups["name"].Value)
            .Where(name => !IsBundle(name) && !IsKnown(name, classes))
            .Distinct(StringComparer.Ordinal).ToList();
        var facts = new Dictionary<string, string>(refused.Refusal.Facts ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        var advice = new List<string>();
        foreach (var name in unknown)
        {
            var closest = Closest(name, classes);
            if (closest.Count == 0) continue;
            advice.Add($"\"{name}\" is not a class here; closest: {string.Join(", ", closest.Select(Describe))}.");
            facts["suggestions"] = string.Join(", ", closest.Select(value => value.Token ?? value.Name ?? value.Id));
        }
        if (advice.Count == 0) return refused;
        return QueryRefusalException.PhonPatternRefused(
            refused.Refusal.Message + " " + string.Join(" ", advice) + " Run motif query --values classes to list them.", facts);
    }

    private static string Describe(PhonInventoryClass value) =>
        value.Token is { Length: > 0 } token && value.Name is { Length: > 0 } name && token != name ? $"{token} ({name})" : value.Token ?? value.Name ?? value.Id;

    /// <summary>At most five classes whose label, token or id begins with, contains, or is a few edits from the name, best first.</summary>
    internal static IReadOnlyList<PhonInventoryClass> Closest(string name, IReadOnlyList<PhonInventoryClass> classes)
    {
        var wanted = name.ToLowerInvariant();
        var allowed = Math.Max(2, wanted.Length / 3);
        return classes.Select(value =>
            {
                var best = int.MaxValue;
                foreach (var candidate in new[] { value.Token, value.Name })
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
            .OrderBy(pair => pair.best).ThenBy(pair => (pair.value.Token ?? pair.value.Name ?? "").Length).ThenBy(pair => pair.value.Token ?? pair.value.Name, StringComparer.Ordinal)
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

    // A literal /.../ is skipped as a whole; a / followed by a space is an environment's separator, not a literal.
    [GeneratedRegex(@"(?<literal>/(?!\s)[^/]*/)|\[(?<name>[^\]]*)\]")]
    private static partial Regex Brackets();
}
