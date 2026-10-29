using System.Globalization;
using System.Text.RegularExpressions;

namespace SIL.Motif.App;

/// <summary>
/// Formats a count sentence from a plural pattern kept as a .resx value, such as
/// <c>{0, plural, one {# change} other {# changes}}</c>; a plain value goes through <c>string.Format</c> instead.
/// The syntax is the ICU MessageFormat plural subset: argument 0 only, the selectors <c>=N</c>, zero, one, two, few,
/// many and other, and <c>#</c> for the count. It has no apostrophe quoting, no nesting and no offset or select.
/// Branches are chosen by the CLDR cardinal rules in <see cref="Rules"/>, copied by hand from
/// https://github.com/unicode-org/cldr/blob/main/common/supplemental/plurals.xml for every FieldWorks UI language.
/// A culture with no entry uses its parent culture's rule, then <c>other</c>; the invariant culture counts as English. A malformed pattern throws a
/// <see cref="FormatException"/> from <see cref="Validate"/>, and from <see cref="Format"/> in Debug builds only.
/// </summary>
public static partial class PluralText
{
    [GeneratedRegex(@"^\s*\{\s*0\s*,\s*plural\s*,(?<branches>.*)\}\s*$", RegexOptions.Singleline)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\G\s*(?<selector>=\d+|zero|one|two|few|many|other)\s*\{(?<text>[^{}]*)\}")]
    private static partial Regex Branch();

    private delegate string Rule(long n);

    private static string OtherOnly(long n) => "other";

    private static string OneIsOne(long n) => n == 1 ? "one" : "other";

    private static string OneIsZeroOrOne(long n) => n is 0 or 1 ? "one" : "other";

    // French and Portuguese: one is 0 and 1; many is a non-zero whole number of millions.
    private static string Romance(long n) =>
        n is 0 or 1 ? "one" : n % 1_000_000 == 0 ? "many" : "other";

    // The magnitude of a count; long.MinValue has none that fits, so it takes the largest value.
    private static long Magnitude(long n) => n == long.MinValue ? long.MaxValue : Math.Abs(n);

    // European Portuguese: one is exactly 1, unlike Brazilian Portuguese.
    private static string PortugalPortuguese(long n) => n == 1 ? "one" : n != 0 && n % 1_000_000 == 0 ? "many" : "other";

    private static string Spanish(long n) => n == 1 ? "one" : n != 0 && n % 1_000_000 == 0 ? "many" : "other";

    private static string Slavic(long n)
    {
        var tail = n % 10;
        var pair = n % 100;
        if (tail == 1 && pair != 11) return "one";
        return tail is >= 2 and <= 4 && pair is not (>= 12 and <= 14) ? "few" : "many";
    }

    private static string Polish(long n) => n == 1 ? "one" : Slavic(n) is "few" ? "few" : "many";

    private static string Arabic(long n)
    {
        var pair = n % 100;
        return n switch
        {
            0 => "zero",
            1 => "one",
            2 => "two",
            _ when pair is >= 3 and <= 10 => "few",
            _ when pair is >= 11 and <= 99 => "many",
            _ => "other",
        };
    }

    /// <summary>The cardinal rule for each language, keyed by the language part of the culture name.</summary>
    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = OneIsOne, ["de"] = OneIsOne, ["az"] = OneIsOne, ["hu"] = OneIsOne, ["ml"] = OneIsOne,
        ["ne"] = OneIsOne, ["sw"] = OneIsOne, ["ta"] = OneIsOne, ["te"] = OneIsOne, ["tr"] = OneIsOne,
        ["ur"] = OneIsOne, ["es"] = Spanish,
        ["fr"] = Romance, ["pt"] = Romance, ["pt-PT"] = PortugalPortuguese,
        ["bn"] = OneIsZeroOrOne, ["fa"] = OneIsZeroOrOne, ["hi"] = OneIsZeroOrOne,
        ["ru"] = Slavic, ["pl"] = Polish, ["ar"] = Arabic,
        ["id"] = OtherOnly, ["ms"] = OtherOnly, ["zlm"] = OtherOnly, ["ko"] = OtherOnly, ["zh"] = OtherOnly,
        ["ja"] = OtherOnly, ["vi"] = OtherOnly, ["th"] = OtherOnly, ["km"] = OtherOnly, ["my"] = OtherOnly,
        ["yo"] = OtherOnly, ["rw"] = OtherOnly,
    };

    /// <summary>The language codes that have a rule here.</summary>
    public static IReadOnlyCollection<string> SupportedLanguages => Rules.Keys;

    /// <summary>The CLDR cardinal category of <paramref name="count"/> in a language code such as "ru".</summary>
    public static string CategoryOf(long count, string language) =>
        Rules.TryGetValue(language, out var rule) ? rule(Magnitude(count)) : "other";

    private static string Category(long count, CultureInfo culture)
    {
        for (var c = culture; c.Name.Length > 0; c = c.Parent)
        {
            if (Rules.TryGetValue(c.Name, out var exact)) return exact(Magnitude(count));
            if (Rules.TryGetValue(c.Name.Split('-')[0], out var rule)) return rule(Magnitude(count));
        }

        // The invariant culture reads the neutral English resx, so it counts as English.
        return culture.Name.Length == 0 ? OneIsOne(Magnitude(count)) : "other";
    }

    /// <summary>Whether <paramref name="value"/> is a plural pattern rather than a plain format string.</summary>
    public static bool IsPattern(string value) => Pattern().IsMatch(value);

    /// <summary>Throws a <see cref="FormatException"/> unless <paramref name="pattern"/> is a whole, valid pattern.</summary>
    public static void Validate(string pattern) => Parse(pattern);

    private static Dictionary<string, string> Parse(string pattern)
    {
        var match = Pattern().Match(pattern);
        if (!match.Success)
            throw new FormatException($"Not a plural pattern: {pattern}");

        var body = match.Groups["branches"].Value;
        var branches = new Dictionary<string, string>(StringComparer.Ordinal);
        var at = 0;
        while (Branch().Match(body, at) is { Success: true } branch)
        {
            if (!branches.TryAdd(branch.Groups["selector"].Value, branch.Groups["text"].Value))
                throw new FormatException($"Plural branch {branch.Groups["selector"].Value} appears twice: {pattern}");
            at = branch.Index + branch.Length;
        }

        if (body[at..].Trim().Length > 0)
            throw new FormatException($"Unparsed text in a plural pattern after '{body[..at]}': {pattern}");
        if (!branches.ContainsKey("other"))
            throw new FormatException($"A plural pattern needs an other branch: {pattern}");
        return branches;
    }

    /// <summary>Picks the branch for <paramref name="count"/> and replaces <c>#</c> with the formatted count.</summary>
    public static string Format(string pattern, long count, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        Dictionary<string, string> branches;
        try
        {
            branches = Parse(pattern);
        }
        catch (FormatException) when (!DebugBuild)
        {
            return pattern;
        }

        var text = branches.TryGetValue("=" + count.ToString(CultureInfo.InvariantCulture), out var exact) ? exact
            : branches.TryGetValue(Category(count, culture), out var chosen) ? chosen
            : branches["other"];
        return text.Replace("#", count.ToString("N0", culture), StringComparison.Ordinal);
    }

    private static readonly bool DebugBuild = IsDebugBuild();

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
