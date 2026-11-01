using System.Text.Json;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The window's words for a saved Difference's codes. A <c>delta.*</c> category code or an approval-state code is
/// a stable wire identifier, never shown; the reader sees the plain label here instead.
/// </summary>
public static class ProposalDifferenceWords
{
    /// <summary>The categories that mean a word may have got worse, in display order.</summary>
    public static IReadOnlyList<ProposalDifferenceBucket> CouldBeBad { get; } =
    [
        ProposalDifferenceBucket.Regression, ProposalDifferenceBucket.NewCompetingParse,
        ProposalDifferenceBucket.Overgeneration, ProposalDifferenceBucket.NewWrongReading,
        ProposalDifferenceBucket.AnalysisChanged, ProposalDifferenceBucket.AmbiguityBlowup,
        ProposalDifferenceBucket.LostParse,
    ];

    /// <summary>The categories that mean a word improved, in display order.</summary>
    public static IReadOnlyList<ProposalDifferenceBucket> Improved { get; } =
    [
        ProposalDifferenceBucket.ImprovedApprovedMatch, ProposalDifferenceBucket.ImprovedUnique,
        ProposalDifferenceBucket.ImprovedNegativeRejection,
    ];

    /// <summary>The label of a category.</summary>
    public static string Bucket(ProposalDifferenceBucket bucket) => bucket switch
    {
        ProposalDifferenceBucket.Regression => MotifAppStrings.DifferenceBucketRegression,
        ProposalDifferenceBucket.NewCompetingParse => MotifAppStrings.DifferenceBucketNewCompetingParse,
        ProposalDifferenceBucket.Overgeneration => MotifAppStrings.DifferenceBucketOvergeneration,
        ProposalDifferenceBucket.NewWrongReading => MotifAppStrings.DifferenceBucketNewWrongReading,
        ProposalDifferenceBucket.AnalysisChanged => MotifAppStrings.DifferenceBucketAnalysisChanged,
        ProposalDifferenceBucket.AmbiguityBlowup => MotifAppStrings.DifferenceBucketAmbiguityBlowup,
        ProposalDifferenceBucket.LostParse => MotifAppStrings.DifferenceBucketLostParse,
        ProposalDifferenceBucket.ImprovedApprovedMatch => MotifAppStrings.DifferenceBucketImprovedApprovedMatch,
        ProposalDifferenceBucket.ImprovedUnique => MotifAppStrings.DifferenceBucketImprovedUnique,
        _ => MotifAppStrings.DifferenceBucketImprovedNegativeRejection,
    };

    /// <summary>The label of a search limit.</summary>
    public static string Limitation(HumanAgreementLimitation limitation) => limitation switch
    {
        HumanAgreementLimitation.Capped => MotifAppStrings.DifferenceLimitationCapped,
        HumanAgreementLimitation.TimedOut => MotifAppStrings.DifferenceLimitationTimedOut,
        HumanAgreementLimitation.ProjectionUnavailable => MotifAppStrings.DifferenceLimitationProjectionUnavailable,
        HumanAgreementLimitation.ParserRefused => MotifAppStrings.DifferenceLimitationParserRefused,
        HumanAgreementLimitation.InvalidShape => MotifAppStrings.DifferenceLimitationInvalidShape,
        HumanAgreementLimitation.MalformedEvidence => MotifAppStrings.DifferenceLimitationMalformedEvidence,
        _ => MotifAppStrings.DifferenceLimitationInvalidExpectedMorphology,
    };

    /// <summary>The label of an approval state given by its wire code; an unknown code reads as Unavailable.</summary>
    public static string State(string code) => code switch
    {
        "no-parse" => MotifAppStrings.DifferenceStateNoParse,
        "no-approved-match" => MotifAppStrings.DifferenceStateNoApprovedMatch,
        "approved-with-alternatives" => MotifAppStrings.DifferenceStateApprovedWithAlternatives,
        "approved-unique" => MotifAppStrings.DifferenceStateApprovedUnique,
        "no-approved-expectations" => MotifAppStrings.DifferenceStateNoApprovedExpectations,
        "incomplete" => MotifAppStrings.DifferenceStateIncomplete,
        "not-measured" => MotifAppStrings.DifferenceStateNotMeasured,
        "conflicting-decisions" => MotifAppStrings.DifferenceStateConflictingDecisions,
        _ => MotifAppStrings.DifferenceStateUnavailable,
    };

    /// <summary>
    /// The window's notes for a summary's recorded warnings. A warning is English for the command line and names the
    /// parser's identity, so the window recognises each one the product writes by its opening and says it plainly;
    /// anything else (such as a note from an older version) becomes one general note, shown once.
    /// </summary>
    public static IReadOnlyList<string> Notes(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0) return [];
        var notes = new List<string>();
        var unknown = false;
        foreach (var warning in warnings)
        {
            // These two are said at the top of the section, where they cannot be missed.
            if (warning.StartsWith(ProposalDifferenceWarnings.SourceValidated, StringComparison.Ordinal) ||
                warning.StartsWith(ProposalDifferenceWarnings.EvidenceUnreadable, StringComparison.Ordinal))
                continue;
            var note = Note(warning);
            if (note is null) unknown = true;
            else if (!notes.Contains(note)) notes.Add(note);
        }
        if (unknown) notes.Add(MotifAppStrings.ProposalDifferenceNoteOther);
        return notes;
    }

    private static string? Note(string warning) =>
        warning.StartsWith(ProposalDifferenceWarnings.ParserDiffers, StringComparison.Ordinal)
            ? MotifAppStrings.ProposalDifferenceNoteParser :
        warning.StartsWith(ProposalDifferenceWarnings.TokeniserDiffers, StringComparison.Ordinal)
            ? MotifAppStrings.ProposalDifferenceNoteTokeniser :
        warning.StartsWith(ProposalDifferenceWarnings.RetryDiffers, StringComparison.Ordinal)
            ? MotifAppStrings.ProposalDifferenceNoteRetry : null;

    /// <summary>The intro line under the heading, true for the decisions the Difference was read against.</summary>
    public static string Advice(ProposalDifferenceSummary? summary) =>
        summary?.Reference == ProposalDifferenceReferences.SourceValidated
            ? MotifAppStrings.ProposalDifferenceAdviceSourceValidated : MotifAppStrings.ProposalDifferenceAdvice;

    /// <summary>The line at the top that says the decisions are a named program's, or nothing for a person's.</summary>
    public static string ReferenceNote(ProposalDifferenceSummary? summary) =>
        summary?.Reference == ProposalDifferenceReferences.SourceValidated
            ? MotifAppStrings.ProposalDifferenceNoteSourceValidated : string.Empty;

    /// <summary>
    /// Names the Applied Proposals whose staged decisions could not be read, up to three. A page with no words reads
    /// none, so the saved warning says it in general words instead.
    /// </summary>
    public static string Unreadable(IReadOnlyList<ProposalDifferenceUnreadableEvidence> unreadable,
        IReadOnlyList<string>? savedWarnings = null)
    {
        if (unreadable.Count == 0)
            return savedWarnings?.Any(warning => warning.StartsWith(ProposalDifferenceWarnings.EvidenceUnreadable,
                StringComparison.Ordinal)) == true ? MotifAppStrings.ProposalDifferenceEvidenceUnreadableSaved : string.Empty;
        return string.Format(System.Globalization.CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalDifferenceEvidenceUnreadableFormat,
            string.Join(", ", unreadable.Take(3).Select(item => item.Label is { Length: > 0 } ? item.Label : item.ProposalId)) +
            (unreadable.Count > 3 ? " …" : string.Empty));
    }

    /// <summary>One line saying why a word's decisions were staged; empty for a person's decisions.</summary>
    public static string Evidence(IReadOnlyList<ProposalDifferenceWordEvidence>? evidence) =>
        evidence is not { Count: > 0 } ? string.Empty : string.Format(System.Globalization.CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalDifferenceEvidenceFormat,
            string.Join(" · ", evidence.Take(MostEvidenceItems).Select(EvidenceItem)) +
            (evidence.Count > MostEvidenceItems ? " …" : string.Empty));

    private const int MostEvidenceItems = 5;

    private static string EvidenceItem(ProposalDifferenceWordEvidence item)
    {
        var tier = EvidenceTier(item.Tier);
        if (item.Level == "wordform") return string.Format(System.Globalization.CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalDifferenceEvidenceWordFormat, tier);
        return item.Agent is null
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                MotifAppStrings.ProposalDifferenceEvidenceAnalysisFormat, tier)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                MotifAppStrings.ProposalDifferenceEvidenceAgentFormat, tier, item.Agent, item.Opinion);
    }

    /// <summary>The label of an evidence tier given by its wire code.</summary>
    public static string EvidenceTier(string tier) => tier switch
    {
        "source-approved" => MotifAppStrings.DifferenceEvidenceTierSourceApproved,
        "valid-attested" => MotifAppStrings.DifferenceEvidenceTierValidAttested,
        "valid-productive" => MotifAppStrings.DifferenceEvidenceTierValidProductive,
        "misspelled-typo" => MotifAppStrings.DifferenceEvidenceTierTypo,
        "ill-formed" => MotifAppStrings.DifferenceEvidenceTierIllFormed,
        "nonstandard-variant" => MotifAppStrings.DifferenceEvidenceTierNonstandardVariant,
        "join-split" => MotifAppStrings.DifferenceEvidenceTierJoinSplit,
        "name-foreign-number" => MotifAppStrings.DifferenceEvidenceTierNameForeignNumber,
        _ => MotifAppStrings.DifferenceEvidenceTierUndecided,
    };

    /// <summary>"Before → After" for two approval-state codes.</summary>
    public static string Moved(string before, string after) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceStatesFormat,
            State(before), State(after));

    /// <summary>The code a request carries for a category or search limit.</summary>
    public static string Code<T>(T value) where T : struct, Enum
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StrictStringEnumConverter<T>());
        return JsonSerializer.Serialize(value, options).Trim('"');
    }
}
