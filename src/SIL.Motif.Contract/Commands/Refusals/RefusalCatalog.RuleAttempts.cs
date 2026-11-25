using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRuleAttempts() =>
    [
        Spec(RefusalCodes.RuleAttemptsInvalid, FailureReason.Refused),
        Spec(RefusalCodes.RuleAttemptsNoBaseline, FailureReason.Refused),
        Spec(RefusalCodes.RuleAttemptsPlanInvalid, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesRuleAttempts() =>
    [
        RefusalCodes.RuleAttemptsInvalid,
        RefusalCodes.RuleAttemptsNoBaseline,
        RefusalCodes.RuleAttemptsPlanInvalid,
    ];
}
