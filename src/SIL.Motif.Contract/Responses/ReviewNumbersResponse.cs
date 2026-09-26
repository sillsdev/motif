using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>Whether a Trial's correctness could be compared with the earlier Correctness Assessment.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewComparability>))]
public enum ReviewComparability
{
    /// <summary>There was no earlier Correctness Assessment, so only the Trial's own counts exist.</summary>
    NoEarlierAssessment,

    /// <summary>The two Assessments were made by different Assessors, so ADR 0042 forbids comparing them.</summary>
    DifferentAssessor,

    /// <summary>The two Assessments measured no word in common.</summary>
    NoSharedWords,

    /// <summary>The two Assessments were compared over the words they share.</summary>
    Compared,
}

/// <summary>
/// What one Trial did to the approved analyses of the touched words, as numbers rather than a sentence,
/// so the CLI and the App each word it for their own reader.
/// </summary>
/// <param name="Comparability">Whether, and why not, the Trial was compared with the earlier Assessment.</param>
/// <param name="SharedWordCount">
/// The number of words both Assessments selected. Zero unless <see cref="Comparability"/> is
/// <see cref="ReviewComparability.Compared"/>.
/// </param>
/// <param name="ApprovedKeptBefore">
/// Among the shared words, how many kept an approved analysis in the earlier Assessment. Zero unless
/// <see cref="Comparability"/> is <see cref="ReviewComparability.Compared"/>.
/// </param>
/// <param name="ApprovedKeptAfter">
/// Among the shared words, how many kept an approved analysis in the Trial. Zero unless
/// <see cref="Comparability"/> is <see cref="ReviewComparability.Compared"/>.
/// </param>
/// <param name="TouchedWordCount">The number of touched words the Trial measured, in every case.</param>
/// <param name="TouchedWordsCovered">
/// How many of the measured touched words kept their approved analyses in the Trial, in every case.
/// </param>
/// <param name="EvidenceComplete">
/// Whether every requested word finished with correctness evidence: none capped, timed out, or of an
/// invalid shape, and none missing.
/// </param>
public sealed record ReviewNumbersResponse(
    ReviewComparability Comparability,
    int SharedWordCount,
    int ApprovedKeptBefore,
    int ApprovedKeptAfter,
    int TouchedWordCount,
    int TouchedWordsCovered,
    bool EvidenceComplete)
{
    /// <summary>
    /// The shared words that matched an approved analysis in the earlier Assessment and no longer match it in
    /// the Trial, in the Trial's word order: applying these changes would lose that approved analysis. The
    /// window disables Apply while this is non-empty, pinned by `ALostApprovedAnalysisDisablesApplyBeforeAnyClick`.
    /// Empty unless <see cref="Comparability"/> is <see cref="ReviewComparability.Compared"/>.
    /// </summary>
    public IReadOnlyList<string> WordsLosingApprovedAnalysis { get; init; } = [];
}
