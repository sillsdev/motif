using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsQuery() =>
    [
        Spec(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryTooManyGroups, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryNotMeasured, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryOpTypeMismatch, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryNotYetAvailable, FailureReason.Refused),
        Spec(RefusalCodes.QueryRealizesNegationIncomplete, FailureReason.Refused),
        Spec(RefusalCodes.QueryRealizesNoParses, FailureReason.Refused),
        Spec(RefusalCodes.QueryRealizesIdUnknown, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryBaselineSameGrammar, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryTwinNeedsRegex, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryTwinGroupUnknown, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryTwinRegexAmbiguous, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryRealizesIndexFailed, FailureReason.Refused),
        Spec(RefusalCodes.QueryRealizesNeedsProject, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryReadingsNeedsGrammar, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryReadingsNeedsProject, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryRetireIdUnknown, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryRealizesGrammarUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.QueryRealizesNeedsIdentity, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryDocumentInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryMixedUnits, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryNoTrialRows, FailureReason.NotFound),
        Spec(RefusalCodes.QueryAsOfUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryWriteWordsExists, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QuerySeqNegationIncomplete, FailureReason.Refused),
        Spec(RefusalCodes.QuerySeqItemUnknown, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryPhonNeedsGrammar, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryPhonUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.QueryPhonPatternRefused, FailureReason.InvalidArgument),
        Spec(RefusalCodes.QueryPhonFailed, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesQuery() =>
    [
        RefusalCodes.QueryAsOfUnavailable,
        RefusalCodes.QueryInvalid,
        RefusalCodes.QueryNoTrialRows,
    ];
}
