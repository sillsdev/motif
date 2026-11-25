using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRuleNotation() =>
    [
        Spec(RefusalCodes.RuleNotationReadOnly, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationSyntax, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationBadRepeat, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationUnknownPhoneme, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationUnknownNaturalClass, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationAmbiguousLabel, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationAlphaMismatch, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationIndexOutOfRange, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationDanglingIndex, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationContextNotAllowed, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationLimitExceeded, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationUnsupportedClass, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationUnrenderable, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationSharedClass, FailureReason.Refused),
        Spec(RefusalCodes.RuleNotationContextHasData, FailureReason.Refused),
    ];
}
