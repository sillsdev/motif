using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsOperation() =>
    [
        Spec(RefusalCodes.OperationCascadingDelete, FailureReason.Refused),
        Spec(RefusalCodes.OperationInvalidDependency, FailureReason.InvalidArgument),
        Spec(RefusalCodes.OperationInvalidId, FailureReason.InvalidArgument),
        Spec(RefusalCodes.OperationInvalidTarget, FailureReason.InvalidArgument),
        Spec(RefusalCodes.OperationInvalidWritingSystem, FailureReason.InvalidArgument),
    ];

    private static IEnumerable<string> WindowCodesOperation() =>
    [
        RefusalCodes.OperationCascadingDelete,
        RefusalCodes.OperationInvalidDependency,
    ];
}
