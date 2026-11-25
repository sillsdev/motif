using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsLexicon() =>
    [
        Spec(RefusalCodes.LexiconUsedByAmbiguous, FailureReason.InvalidArgument),
    ];
}
