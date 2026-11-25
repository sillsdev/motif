using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsProgress() =>
    [
        Spec(RefusalCodes.ProgressIndexNotReady, FailureReason.Refused),
        Spec(RefusalCodes.ProgressInvalidCursor, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProgressInvalidRequest, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProgressRevisionChanged, FailureReason.Refused),
        Spec(RefusalCodes.ProgressTextNotFound, FailureReason.NotFound),
    ];
}
