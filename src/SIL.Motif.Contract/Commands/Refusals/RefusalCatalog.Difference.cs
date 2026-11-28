using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsDifference() =>
    [
        Spec(RefusalCodes.DifferenceBaselineChanged, FailureReason.Refused),
        Spec(RefusalCodes.DifferenceCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.DifferenceDuplicateParseRows, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.DifferenceInvalidBucket, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceInvalidLimitation, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceInvalidProposal, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceInvalidReference, FailureReason.InvalidArgument,
            NextStep.Repeat(("reference", NextArg.Literal("human")))),
        Spec(RefusalCodes.DifferenceInvalidState, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceInvalidText, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceInvalidWord, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceNoBaseline, FailureReason.NotFound),
        Spec(RefusalCodes.DifferenceNoBefore, FailureReason.Refused),
        Spec(RefusalCodes.DifferenceNoParseAssessment, FailureReason.Refused),
        Spec(RefusalCodes.DifferenceNoTrial, FailureReason.NotFound),
        Spec(RefusalCodes.DifferenceNone, FailureReason.NotFound),
        Spec(RefusalCodes.DifferenceProposalDecided, FailureReason.Refused),
        Spec(RefusalCodes.DifferenceTextNeedsProposal, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DifferenceTextNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.DifferenceTrialIncomplete, FailureReason.Refused),
        Spec(RefusalCodes.DifferenceUnexpectedOpinion, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.DifferenceUnreadable, FailureReason.StoreInconsistent),
    ];

    private static IEnumerable<string> WindowCodesDifference() =>
    [
        RefusalCodes.DifferenceBaselineChanged,
        RefusalCodes.DifferenceCancelled,
        RefusalCodes.DifferenceDuplicateParseRows,
        RefusalCodes.DifferenceNoBaseline,
        RefusalCodes.DifferenceNoBefore,
        RefusalCodes.DifferenceNoParseAssessment,
        RefusalCodes.DifferenceNoTrial,
        RefusalCodes.DifferenceProposalDecided,
        RefusalCodes.DifferenceTextNotFound,
        RefusalCodes.DifferenceTrialIncomplete,
        RefusalCodes.DifferenceUnexpectedOpinion,
        RefusalCodes.DifferenceUnreadable,
    ];
}
