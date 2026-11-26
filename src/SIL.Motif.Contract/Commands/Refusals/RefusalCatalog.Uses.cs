using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsUses() =>
    [
        Spec(RefusalCodes.UsesInvalidRequest, FailureReason.InvalidArgument),
        Spec(RefusalCodes.UsesNoAssessment, FailureReason.NotFound),
    ];
}
