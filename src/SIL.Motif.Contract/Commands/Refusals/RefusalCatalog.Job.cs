using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsJob() =>
    [
        Spec(RefusalCodes.JobParserUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.JobWaitIdentityMismatch, FailureReason.InvalidArgument),
        Spec(RefusalCodes.JobWaitKindUnsupported, FailureReason.InvalidArgument),
        Spec(RefusalCodes.JobWaitCleanupUnconfirmed, FailureReason.Busy),
        Spec(RefusalCodes.JobWaitCancelled, FailureReason.Cancelled,
            NextStep.Repeat(("job", NextArg.Fact("jobId")))),
        Spec(RefusalCodes.JobWaitTimeout, FailureReason.Busy,
            NextStep.Repeat(("job", NextArg.Fact("jobId")))),
        Spec(RefusalCodes.JobAlreadyFinished, FailureReason.Refused),
        Spec(RefusalCodes.JobAssessmentsInconsistent, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.JobDryRunIncomplete, FailureReason.Refused,
            Command("dry-run", Caller("project"))),
        Spec(RefusalCodes.JobInvalidId, FailureReason.InvalidArgument),
        Spec(RefusalCodes.JobInvalidMove, FailureReason.InvalidArgument),
        Spec(RefusalCodes.JobInvalidWords, FailureReason.InvalidArgument),
        Spec(RefusalCodes.JobMoveTargetNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.JobNoAssessments, FailureReason.Refused),
        Spec(RefusalCodes.JobNotFinished, FailureReason.Refused),
        Spec(RefusalCodes.JobNotFound, FailureReason.NotFound),
    ];

    private static IEnumerable<string> WindowCodesJob() =>
    [
        RefusalCodes.JobParserUnavailable,
        RefusalCodes.JobWaitCancelled,
        RefusalCodes.JobWaitTimeout,
    ];
}
