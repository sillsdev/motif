using System.Text;

namespace SIL.Motif.Contract.Responses;

/// <summary>Compares ordered word analyses by their morphological identity under ADR 0027.</summary>
public static class AnalysisMorphologyMatcher
{
    /// <summary>
    /// Compares a parser reading with a stored morphology by bundle count and ordered Form, MSA, and
    /// inflection-type identities. When the parser supplies guessed text, any stored form alternative may match
    /// after both strings are normalized to NFD.
    /// </summary>
    public static bool Matches(ParseAnalysis actual, ApprovedMorphology expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        return actual.Morphs.Count > 0 && actual.Morphs.Count == expected.Morphs.Count &&
            actual.Morphs.Zip(expected.Morphs).All(pair =>
                pair.First.Form == pair.Second.Form && pair.First.Msa == pair.Second.Msa &&
                pair.First.InflType == pair.Second.InflType &&
                (pair.First.GuessedString is null || pair.Second.Forms.Any(form =>
                    string.Equals(Nfd(form), Nfd(pair.First.GuessedString), StringComparison.Ordinal))));
    }

    private static string Nfd(string value) => value.Normalize(NormalizationForm.FormD);
}
