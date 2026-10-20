using System.Globalization;
using System.Text.RegularExpressions;

namespace SIL.Motif.App;

/// <summary>
/// Formats a count sentence from an ICU-style plural pattern kept as a .resx value, such as
/// <c>{0, plural, one {# change} other {# changes}}</c>. A value without a pattern is not passed here: it goes
/// through <see cref="string.Format(IFormatProvider, string, object?[])"/> as usual. Only the English plural rule
/// (one for exactly 1, otherwise other) is built, so a translated language with more categories needs a rule added.
/// </summary>
public static partial class PluralText
{
    [GeneratedRegex(@"^\s*\{\s*0\s*,\s*plural\s*,(?<branches>.*)\}\s*$", RegexOptions.Singleline)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"(?<selector>=\d+|zero|one|two|few|many|other)\s*\{(?<text>[^{}]*)\}")]
    private static partial Regex Branch();

    /// <summary>Whether <paramref name="value"/> is a plural pattern rather than a plain format string.</summary>
    public static bool IsPattern(string value) => Pattern().IsMatch(value);

    /// <summary>Picks the branch for <paramref name="count"/> and replaces <c>#</c> with the formatted count.</summary>
    public static string Format(string pattern, int count, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var match = Pattern().Match(pattern);
        if (!match.Success)
            throw new FormatException($"Not a plural pattern: {pattern}");

        var branches = Branch().Matches(match.Groups["branches"].Value)
            .ToDictionary(m => m.Groups["selector"].Value, m => m.Groups["text"].Value, StringComparer.Ordinal);
        if (!branches.ContainsKey("other"))
            throw new FormatException($"A plural pattern needs an other branch: {pattern}");

        var text = branches.TryGetValue("=" + count.ToString(CultureInfo.InvariantCulture), out var exact) ? exact
            : count == 1 && branches.TryGetValue("one", out var one) ? one
            : branches["other"];
        return text.Replace("#", count.ToString("N0", culture), StringComparison.Ordinal);
    }
}
