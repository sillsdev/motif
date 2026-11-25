using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWhyNot() =>
    [
        Spec(RefusalCodes.WhyNotRequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.WhyNotParserUnsupported, FailureReason.Refused),
        Spec(RefusalCodes.WhyNotInputRefused, FailureReason.Refused),
        Spec(RefusalCodes.WhyNotMalformed, FailureReason.Refused),
        Spec(RefusalCodes.WhyNotUnfinished, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesWhyNot() =>
    [
        RefusalCodes.WhyNotRequestInvalid,
        RefusalCodes.WhyNotParserUnsupported,
        RefusalCodes.WhyNotInputRefused,
        RefusalCodes.WhyNotMalformed,
        RefusalCodes.WhyNotUnfinished,
    ];
}
