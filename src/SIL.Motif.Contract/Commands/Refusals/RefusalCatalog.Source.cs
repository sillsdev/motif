using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsSource() =>
    [
        Spec(RefusalCodes.SourceIndexMissing, FailureReason.NotFound),
        Spec(RefusalCodes.SourceIndexStale, FailureReason.Refused),
        Spec(RefusalCodes.SourcePassageLimitInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourcePassageCursorInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceGenerationChanged, FailureReason.Refused),
        Spec(RefusalCodes.SourceIdInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceListAmbiguous, FailureReason.Refused),
        Spec(RefusalCodes.SourceNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.SourceRefInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceFolderMissing, FailureReason.NotFound),
        Spec(RefusalCodes.SourceChapterNotInSource, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceIdInUse, FailureReason.Refused),
        Spec(RefusalCodes.SourceIdRemoved, FailureReason.Refused),
        Spec(RefusalCodes.SourceContextInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceVersificationUnknown, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceKindUnsupported, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceOccurrenceNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.SourceOccurrenceNotEligible, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceAnalysisMismatch, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceAnalysisNeeded, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SourceWordformMissing, FailureReason.NotFound),
        Spec(RefusalCodes.SourceAiNeedsProposal, FailureReason.Refused),
    ];
}
