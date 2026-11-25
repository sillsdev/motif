using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsMorphType() =>
    [
        Spec(RefusalCodes.MorphTypeIncompatibleFamily, FailureReason.Refused),
        Spec(RefusalCodes.MorphTypeStaleSource, FailureReason.Refused),
    ];
}
