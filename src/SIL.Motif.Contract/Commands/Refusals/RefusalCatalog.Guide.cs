using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsGuide() =>
    [
        Spec(RefusalCodes.GdrAmbiguous, FailureReason.Refused),
        Spec(RefusalCodes.GdrBaselineChanged, FailureReason.Refused),
        Spec(RefusalCodes.GdrBaselineUnavailable, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.GdrEmptyId, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrLinkAmbiguous, FailureReason.Refused),
        Spec(RefusalCodes.GdrLinkNoTool, FailureReason.Refused),
        Spec(RefusalCodes.GdrLinkNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.GdrLinkPathInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrNoBaseline, FailureReason.NotFound),
        Spec(RefusalCodes.GdrNotFound, FailureReason.NotFound, Command("guide list", Caller("project"))),
        Spec(RefusalCodes.GdrRecordMoved, FailureReason.Refused),
        Spec(RefusalCodes.GdrMarkdownInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrTitleInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrListNotWritable, FailureReason.Refused),
        Spec(RefusalCodes.GdrDecisionInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrDecisionsInvalid, FailureReason.InvalidArgument, NextStep.Repeat()),
        Spec(RefusalCodes.GdrMoveCycle, FailureReason.InvalidArgument, Command("guide list", Caller("project"))),
        Spec(RefusalCodes.GdrMoveStale, FailureReason.Refused,
            Command("guide show", Caller("project"), ("id", NextArg.Caller("id")), ("as_of", NextArg.Fact("draftName")))),
        Spec(RefusalCodes.GdrMovePosition, FailureReason.InvalidArgument, Command("guide list", Caller("project"))),
        Spec(RefusalCodes.GdrStyleCycle, FailureReason.StoreInconsistent),
    ];
}
