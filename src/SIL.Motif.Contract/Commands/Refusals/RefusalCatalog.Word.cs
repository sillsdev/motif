using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWord() =>
    [
        Spec(RefusalCodes.WordReadStateCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.WordReadStateInvalid, FailureReason.InvalidArgument),
    ];
}
