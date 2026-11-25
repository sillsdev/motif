using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsText() =>
    [
        Spec(RefusalCodes.TextFieldStale, FailureReason.Refused),
        Spec(RefusalCodes.TextStyleUnknown, FailureReason.InvalidArgument),
    ];

    private static IEnumerable<string> WindowCodesText() =>
    [
        RefusalCodes.TextFieldStale,
        RefusalCodes.TextStyleUnknown,
    ];
}
