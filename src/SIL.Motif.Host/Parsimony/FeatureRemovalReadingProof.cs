using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// Proves, from the two Assessments of one Trial, that removing an exception feature left the readings of the words
/// that used it unchanged. It is <see cref="ReadingIdentityProof"/> with no retired forms to translate, because the
/// feature composer retires no allomorph: the before and after readings must match as they stand, and a reading the
/// removal newly allows (a rule branch that no longer excludes a stem) is counted as new.
/// </summary>
public static class FeatureRemovalReadingProof
{
    /// <summary>Compares every reading of <paramref name="affectedWords"/> across the Trial's two runs.</summary>
    public static ReadingIdentityResult Prove(RecipeVerificationRun before, RecipeVerificationRun after,
        IEnumerable<string> affectedWords) =>
        ReadingIdentityProof.Prove(before, after, affectedWords, Array.Empty<RetirementExpectationMapping>());
}
