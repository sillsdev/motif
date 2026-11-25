using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsGrammarRead() =>
    [
        Spec(RefusalCodes.GrammarReadBaselineMissing, FailureReason.NotFound),
        Spec(RefusalCodes.GrammarReadBaselineUnavailable, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.GrammarReadInvalidRequest, FailureReason.InvalidArgument),
    ];
}
