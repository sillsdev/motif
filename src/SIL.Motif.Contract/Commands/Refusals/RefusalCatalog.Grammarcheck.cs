using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsGrammarcheck() =>
    [
        Spec(RefusalCodes.GrammarCheckCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.GrammarCheckFailed, FailureReason.Refused),
        Spec(RefusalCodes.GrammarCheckMalformedFindings, FailureReason.Refused),
        Spec(RefusalCodes.GrammarCheckParserRefused, FailureReason.Refused),
        Spec(RefusalCodes.GrammarCheckParserUnavailable, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.GrammarCheckTimedOut, FailureReason.Refused),
        Spec(RefusalCodes.GrammarCheckUnsupportedSchema, FailureReason.Refused),
        Spec(RefusalCodes.GrammarCheckNotRun, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesGrammarcheck() =>
    [
        RefusalCodes.GrammarCheckCancelled,
        RefusalCodes.GrammarCheckMalformedFindings,
        RefusalCodes.GrammarCheckParserRefused,
        RefusalCodes.GrammarCheckParserUnavailable,
        RefusalCodes.GrammarCheckTimedOut,
        RefusalCodes.GrammarCheckUnsupportedSchema,
    ];
}
