using System.Text;

namespace SIL.Motif.Projection.Grammar;

/// <summary>
/// Exact-form comparison shared by the CLI and MCP lookups: equal ignoring case and Unicode normalization form,
/// with a bare form also equal to an affix form that carries its morph-type marker.
/// </summary>
public static class FormMatch
{
    private static readonly char[] AffixMarkers = ['-', '=', '~'];

    /// <summary>Whether <paramref name="candidate"/> is the form <paramref name="wanted"/> names.</summary>
    /// <param name="wanted">The text asked for; a leading or trailing marker must then match exactly.</param>
    /// <param name="candidate">A stored headword, form or name.</param>
    public static bool Same(string? wanted, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(wanted) || candidate is null) return false;
        var target = Fold(wanted);
        var folded = Fold(candidate);
        if (folded == target) return true;
        return target.IndexOfAny(AffixMarkers) != 0 && target.LastIndexOfAny(AffixMarkers) != target.Length - 1 &&
            folded.Trim(AffixMarkers) == target;
    }

    /// <summary>Whether any of <paramref name="candidates"/> is the form <paramref name="wanted"/> names.</summary>
    public static bool Any(string? wanted, IEnumerable<string?> candidates) =>
        candidates.Any(candidate => Same(wanted, candidate));

    private static string Fold(string text) =>
        text.Trim().Normalize(NormalizationForm.FormD).ToLowerInvariant();
}
