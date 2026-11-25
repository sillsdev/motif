using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWhyLost() =>
    [
        Spec(RefusalCodes.WhyLostRequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.WhyLostIncomparable, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesWhyLost() =>
    [
        RefusalCodes.WhyLostRequestInvalid,
        RefusalCodes.WhyLostIncomparable,
    ];
}
