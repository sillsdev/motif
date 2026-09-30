using System.Text.RegularExpressions;

namespace SIL.Motif.Commands;

/// <summary>What a reason for a change that no longer fits says went wrong, for a reader that words it itself.</summary>
public enum ChangeFitReasonKind
{
    /// <summary>A reason this class did not write.</summary>
    Unrecognized,

    /// <summary>The change's recorded evidence is missing or unreadable, so it cannot be checked at all.</summary>
    CannotCheck,

    WordformDeleted,
    WordformChangedForm,
    WordformSpellingChanged,
    AnalysisMissing,
    AnalysisReadingChanged,
    AnalysisOpinionChanged,
    MorphReferenceMissing,
    ReadingAlreadyExists,
    BaselineNotCurrent,
}

/// <summary>
/// The reasons the fit check gives when a collected change no longer fits the saved project, written for the CLI's
/// reader and kept here beside <see cref="KindOf"/>, so a front end that must not show internal ids can word a
/// reason by its kind without parsing prose written somewhere else.
/// </summary>
public static partial class ChangeFitReasons
{
    public const string FingerprintMissing = "Change fingerprint is missing or invalid.";
    public const string FingerprintMalformed = "Change fingerprint is malformed.";
    public const string FingerprintIncomplete =
        "Change fingerprint has missing or invalid identity, form, or Baseline evidence.";
    public const string AnalysisIdentityInvalid = "Change fingerprint has an invalid analysis identity.";
    public const string SpellingEvidenceMissing =
        "The collected change has no spelling status evidence. Remove it and collect it again.";
    public const string MappingMissing = "Change mapping or fingerprint is missing.";
    public const string BaselineNotCurrent = "The collected change's Baseline is no longer current.";

    public static string WordformDeleted(string wordformId) => $"Wordform {wordformId} was deleted.";
    public static string WordformChangedForm(string wordformId) => $"Wordform {wordformId} changed form.";
    public static string WordformSpellingChanged(string wordformId) => $"Wordform {wordformId} changed spelling status.";
    public static string AnalysisMissing(string analysisId, string wordformId) =>
        $"Analysis {analysisId} was deleted or moved from wordform {wordformId}.";
    public static string AnalysisReadingChanged(string analysisId) => $"Analysis {analysisId} changed its reading.";
    public static string AnalysisOpinionChanged(string analysisId) => $"Analysis {analysisId} changed its human opinion.";
    public static string MorphReferenceMissing(string morphId) =>
        $"Candidate morph reference {morphId} was deleted or changed type.";
    public static string ReadingAlreadyExists(string wordformId) =>
        $"The parser reading already exists under wordform {wordformId}.";

    /// <summary>The kind of <paramref name="reason"/>, or <see cref="ChangeFitReasonKind.Unrecognized"/>.</summary>
    public static ChangeFitReasonKind KindOf(string? reason) => reason switch
    {
        null => ChangeFitReasonKind.Unrecognized,
        FingerprintMissing or FingerprintMalformed or FingerprintIncomplete or AnalysisIdentityInvalid
            or SpellingEvidenceMissing or MappingMissing => ChangeFitReasonKind.CannotCheck,
        BaselineNotCurrent => ChangeFitReasonKind.BaselineNotCurrent,
        _ when WordformDeletedPattern().IsMatch(reason) => ChangeFitReasonKind.WordformDeleted,
        _ when WordformChangedFormPattern().IsMatch(reason) => ChangeFitReasonKind.WordformChangedForm,
        _ when WordformSpellingChangedPattern().IsMatch(reason) => ChangeFitReasonKind.WordformSpellingChanged,
        _ when AnalysisMissingPattern().IsMatch(reason) => ChangeFitReasonKind.AnalysisMissing,
        _ when AnalysisReadingChangedPattern().IsMatch(reason) => ChangeFitReasonKind.AnalysisReadingChanged,
        _ when AnalysisOpinionChangedPattern().IsMatch(reason) => ChangeFitReasonKind.AnalysisOpinionChanged,
        _ when MorphReferenceMissingPattern().IsMatch(reason) => ChangeFitReasonKind.MorphReferenceMissing,
        _ when ReadingAlreadyExistsPattern().IsMatch(reason) => ChangeFitReasonKind.ReadingAlreadyExists,
        _ => ChangeFitReasonKind.Unrecognized,
    };

    [GeneratedRegex(@"^Wordform \S+ was deleted\.$")]
    private static partial Regex WordformDeletedPattern();

    [GeneratedRegex(@"^Wordform \S+ changed form\.$")]
    private static partial Regex WordformChangedFormPattern();

    [GeneratedRegex(@"^Wordform \S+ changed spelling status\.$")]
    private static partial Regex WordformSpellingChangedPattern();

    [GeneratedRegex(@"^Analysis \S+ was deleted or moved from wordform \S+\.$")]
    private static partial Regex AnalysisMissingPattern();

    [GeneratedRegex(@"^Analysis \S+ changed its reading\.$")]
    private static partial Regex AnalysisReadingChangedPattern();

    [GeneratedRegex(@"^Analysis \S+ changed its human opinion\.$")]
    private static partial Regex AnalysisOpinionChangedPattern();

    [GeneratedRegex(@"^Candidate morph reference \S+ was deleted or changed type\.$")]
    private static partial Regex MorphReferenceMissingPattern();

    [GeneratedRegex(@"^The parser reading already exists under wordform \S+\.$")]
    private static partial Regex ReadingAlreadyExistsPattern();
}
