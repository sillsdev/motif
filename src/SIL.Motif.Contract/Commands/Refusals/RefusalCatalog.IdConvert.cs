using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsIdConvert() =>
    [
        Spec(RefusalCodes.IdConvertInvalidRequest, FailureReason.InvalidArgument),
        Spec(RefusalCodes.IdConvertInvalidId, FailureReason.InvalidArgument),
    ];
}
