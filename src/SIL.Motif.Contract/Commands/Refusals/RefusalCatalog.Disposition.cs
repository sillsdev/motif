using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsDisposition() =>
    [
        Spec(RefusalCodes.DispositionInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.DispositionContradiction, FailureReason.Refused),
    ];
}
