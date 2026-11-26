using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsFormsExplain() =>
    [
        Spec(RefusalCodes.FormsExplainRequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.FormsExplainParserUnsupported, FailureReason.Refused),
        Spec(RefusalCodes.FormsExplainMorphemeUnresolved, FailureReason.NotFound),
        Spec(RefusalCodes.FormsExplainMorphemeAmbiguous, FailureReason.InvalidArgument),
        Spec(RefusalCodes.FormsExplainParserRefused, FailureReason.Refused),
        Spec(RefusalCodes.FormsExplainMalformed, FailureReason.Refused),
        Spec(RefusalCodes.FormsExplainUnfinished, FailureReason.Refused),
        Spec(RefusalCodes.FormsExplainNoReading, FailureReason.NotFound),
    ];

    private static IEnumerable<string> WindowCodesFormsExplain() =>
    [
        RefusalCodes.FormsExplainRequestInvalid,
        RefusalCodes.FormsExplainParserUnsupported,
        RefusalCodes.FormsExplainMorphemeUnresolved,
        RefusalCodes.FormsExplainMorphemeAmbiguous,
        RefusalCodes.FormsExplainParserRefused,
        RefusalCodes.FormsExplainMalformed,
        RefusalCodes.FormsExplainUnfinished,
        RefusalCodes.FormsExplainNoReading,
    ];
}
