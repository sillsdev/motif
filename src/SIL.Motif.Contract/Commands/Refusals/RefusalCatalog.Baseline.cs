using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsBaseline() =>
    [
        Spec(RefusalCodes.BaselineBusy, FailureReason.Busy),
        Spec(RefusalCodes.BaselineCopyUnloadable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.BaselineOwnedRootViolation, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.BaselineSourceIncomplete, FailureReason.InvalidArgument),
        Spec(RefusalCodes.BaselineTextWordsUnreadable, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.BaselineMissing, FailureReason.NotFound,
            Command("baseline capture", Caller("project"))),
    ];

    private static IEnumerable<string> WindowCodesBaseline() =>
    [
        RefusalCodes.BaselineBusy,
        RefusalCodes.BaselineMissing,
        RefusalCodes.BaselineCopyUnloadable,
        RefusalCodes.BaselineOwnedRootViolation,
        RefusalCodes.BaselineSourceIncomplete,
        RefusalCodes.BaselineTextWordsUnreadable,
    ];
}
