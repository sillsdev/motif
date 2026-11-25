using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWordContext() =>
    [
        Spec(RefusalCodes.WordContextBaselineChanged, FailureReason.Refused),
        Spec(RefusalCodes.WordContextBaselineUnavailable, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.WordContextEmptyWord, FailureReason.InvalidArgument),
    ];
}
