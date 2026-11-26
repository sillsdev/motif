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
        Spec(RefusalCodes.GdrNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.GdrRecordMoved, FailureReason.Refused),
        Spec(RefusalCodes.GdrMarkdownInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrTitleInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrListNotWritable, FailureReason.Refused),
        Spec(RefusalCodes.GdrDecisionInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GdrStyleCycle, FailureReason.StoreInconsistent),
    ];
}
