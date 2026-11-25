using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsAssess() =>
    [
        Spec(RefusalCodes.AssessBaselineChanged, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.AssessCollectEmpty, FailureReason.InvalidArgument),
        Spec(RefusalCodes.AssessInvalidLimit, FailureReason.InvalidArgument),
        Spec(RefusalCodes.AssessInvocationInconsistent, FailureReason.Refused),
        Spec(RefusalCodes.AssessMeasurementsIncomplete, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.AssessOutOfMemory, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.AssessParserKilled, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.AssessParserMemoryLimit, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.AssessUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.AssessTimingDetailSelection, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessUnsupportedKind, FailureReason.Refused),
        Spec(RefusalCodes.AssessWordsAmbiguous, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessWordsFileEmpty, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessWordsFileUnreadable, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessWordsFileNotText, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessWordsMultiWord, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessWordsFileTooLarge, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessWordsFileBadRow, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(RefusalCodes.AssessInvalidReplacement, FailureReason.InvalidArgument),
    ];

    private static IEnumerable<string> WindowCodesAssess() =>
    [
        RefusalCodes.AssessBaselineChanged,
        RefusalCodes.AssessInvalidLimit,
        RefusalCodes.AssessInvocationInconsistent,
        RefusalCodes.AssessMeasurementsIncomplete,
        RefusalCodes.AssessParserUnavailable,
        RefusalCodes.AssessUnavailable,
        RefusalCodes.AssessUnsupportedKind,
        RefusalCodes.AssessWordsAmbiguous,
        RefusalCodes.AssessWordsFileEmpty,
        RefusalCodes.AssessWordsMultiWord,
        RefusalCodes.AssessWordsFileUnreadable,
    ];
}
