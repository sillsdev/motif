using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsProposal() =>
    [
        Spec(RefusalCodes.ProposalCompareProposalsInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused),
        Spec(RefusalCodes.ProposalCompareBaselineMismatch, FailureReason.Refused),
        Spec(RefusalCodes.ProposalCompareAtMismatch, FailureReason.Refused),
        Spec(RefusalCodes.ProposalCompareSnapshotChanged, FailureReason.Refused, NextStep.Repeat()),
        Spec(RefusalCodes.ProposalAlreadyApplied, FailureReason.Refused),
        Spec(RefusalCodes.ProposalAppliedFinal, FailureReason.Refused),
        Spec(RefusalCodes.ProposalEvidenceRetained, FailureReason.Refused),
        Spec(RefusalCodes.ProposalClosed, FailureReason.Refused),
        Spec(RefusalCodes.ProposalHasDraft, FailureReason.Refused),
        Spec(RefusalCodes.ProposalInconsistent, FailureReason.Refused),
        Spec(RefusalCodes.ProposalInvalidId, FailureReason.InvalidArgument,
            Command("proposals", Caller("project"))),
        Spec(RefusalCodes.ProposalInvalidStatus, FailureReason.Refused),
        Spec(RefusalCodes.ProposalNotClosed, FailureReason.Refused),
        Spec(RefusalCodes.ProposalNotEditable, FailureReason.Refused),
        Spec(RefusalCodes.ProposalCommentInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProposalPictureInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProposalNotFound, FailureReason.NotFound,
            Command("proposals", Caller("project"))),
        Spec(RefusalCodes.ProposalPendingChanges, FailureReason.Refused),
        Spec(RefusalCodes.ProposalRationaleEmpty, FailureReason.InvalidArgument,
            Command("proposal rationale", Caller("project"), ("proposal", NextArg.Fact("proposalId")))),
        Spec(RefusalCodes.ProposalRationaleFindings, FailureReason.Refused,
            Command("proposal rationale", Caller("project"), ("proposal", NextArg.Fact("proposalId")))),
        Spec(RefusalCodes.ProposalRationaleStale, FailureReason.Refused, NextStep.Repeat()),
        Spec(RefusalCodes.ProposalRationaleUnchecked, FailureReason.Refused, NextStep.Repeat()),
        Spec(RefusalCodes.ProposalRetainedByExampleSet, FailureReason.Refused),
        Spec(RefusalCodes.ProposalSplitDuplicateOperation, FailureReason.Refused),
        Spec(RefusalCodes.ProposalSupersedeSelf, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProposalSuperseded, FailureReason.Refused),
        Spec(RefusalCodes.ProposalTrialRunning, FailureReason.Refused),
        Spec(RefusalCodes.ProposalUnknownStatus, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProposalUnreadable, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesProposal() =>
    [
        RefusalCodes.ProposalClosed,
        RefusalCodes.ProposalCommentInvalid,
        RefusalCodes.ProposalInvalidId,
        RefusalCodes.ProposalInvalidStatus,
        RefusalCodes.ProposalNotFound,
        RefusalCodes.ProposalPictureInvalid,
        RefusalCodes.ProposalRationaleEmpty,
        RefusalCodes.ProposalRationaleFindings,
        RefusalCodes.ProposalRationaleStale,
        RefusalCodes.ProposalRationaleUnchecked,
        RefusalCodes.ProposalUnreadable,
    ];
}
