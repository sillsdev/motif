using System;
using System.Text.RegularExpressions;

namespace SIL.Motif.Contract;

/// <summary>Validates a semantic product version and extracts its numeric compatibility floor.</summary>
public static class ProductVersionSyntax
{
    private const string Number = "(?:0|[1-9][0-9]*)";
    private const string Label = "(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)";
    private static readonly Regex Pattern = new(
        @"\A(?<core>" + Number + @"\." + Number + @"\." + Number + ")" +
        "(?:-" + Label + @"(?:\." + Label + @")*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant);

    /// <summary>Accepts three-part SemVer, including prerelease and build labels, without ordering those labels.</summary>
    /// <param name="text">The full product version to validate.</param>
    /// <param name="core">Its numeric version on success; zero on failure.</param>
    /// <returns>Whether the entire value is a valid product version with a representable numeric core.</returns>
    public static bool TryParseCore(string? text, out Version core)
    {
        core = new Version(0, 0, 0);
        if (text is null) return false;
        var match = Pattern.Match(text);
        if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out var parsed)) return false;
        core = parsed;
        return true;
    }
}
