using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsLexiconFind() =>
    [
        Spec(RefusalCodes.LexiconFindBaselineMissing, FailureReason.NotFound),
        Spec(RefusalCodes.LexiconFindBaselineUnavailable, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.LexiconFindInvalidRequest, FailureReason.InvalidArgument),
    ];
}
