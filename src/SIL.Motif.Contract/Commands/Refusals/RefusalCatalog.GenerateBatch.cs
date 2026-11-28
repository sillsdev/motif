using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsGenerateBatch() =>
    [
        Spec(RefusalCodes.GenerateBatchIo, FailureReason.Refused),
    ];
}
