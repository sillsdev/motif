using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsDraft() =>
    [
        Spec(RefusalCodes.DraftRevisionConflict, FailureReason.Refused,
            Command("show", Caller("project"), ("proposal", NextArg.Fact("draftName")))),
        Spec(RefusalCodes.DraftInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DraftForEachTargetsFileUnavailable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DraftForEachEmpty, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DraftForEachUnknownPlaceholder, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DraftForEachPlaceholderWithoutForEach, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DraftForEachNested, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DraftForEachTargetUnresolved, FailureReason.InvalidArgument),
        Spec(RefusalCodes.VariantComponentRequired, FailureReason.Refused),
        Spec(RefusalCodes.SenseAnalysisContent, FailureReason.Refused),
        Spec(RefusalCodes.DraftAuthoredContent, FailureReason.Refused),
        Spec(RefusalCodes.DraftEvidenceRetained, FailureReason.Refused),
        Spec(RefusalCodes.DraftNameCollision, FailureReason.Refused),
        Spec(RefusalCodes.DraftNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.DraftRetainedByExampleSet, FailureReason.Refused),
        Spec(RefusalCodes.DraftDiscardPendingChanges, FailureReason.Refused),
        Spec(RefusalCodes.DraftTrialRunning, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesDraft() =>
    [
        RefusalCodes.DraftRevisionConflict,
    ];
}
