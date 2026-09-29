using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Analysis;

/// <summary>Compares ordered word analyses by their morphological identity under ADR 0027.</summary>
public static class AnalysisMorphologyMatcher
{
    /// <summary>
    /// Compares a parser reading with a stored morphology by bundle count and ordered Form, MSA, and
    /// inflection-type identities. When the parser supplies guessed text, any stored form alternative may match
    /// after both strings are normalized to NFD.
    /// </summary>
    /// <param name="actual">The parser reading.</param>
    /// <param name="expected">The stored morphology.</param>
    /// <returns>Whether every ordered morph has matching identity and form evidence.</returns>
    public static bool Matches(ParseAnalysis actual, ApprovedMorphology expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        return actual.Morphs.Count == expected.Morphs.Count &&
            actual.Morphs.Zip(expected.Morphs).All(pair =>
                SameIdentity(pair.First.Form, pair.Second.Form) && SameIdentity(pair.First.Msa, pair.Second.Msa) &&
                SameIdentity(pair.First.InflType, pair.Second.InflType) &&
                (pair.First.GuessedString is null || pair.Second.Forms.Any(form =>
                    string.Equals(Nfd(form), Nfd(pair.First.GuessedString), StringComparison.Ordinal))));
    }

    private static bool SameIdentity(string? actual, string? expected)
    {
        if (Guid.TryParse(actual, out var actualGuid) && Guid.TryParse(expected, out var expectedGuid))
            return actualGuid == expectedGuid;
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static string Nfd(string value) => value.Normalize(NormalizationForm.FormD);
}
