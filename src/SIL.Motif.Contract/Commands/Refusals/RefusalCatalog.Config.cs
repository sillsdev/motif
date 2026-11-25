using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsConfig() =>
    [
        Spec(RefusalCodes.ConfigInvalid, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesConfig() =>
    [
        RefusalCodes.ConfigInvalid,
    ];
}
