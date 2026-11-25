using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsJudgment() =>
    [
        Spec(RefusalCodes.JudgmentFieldAmbiguous, FailureReason.Refused),
        Spec(RefusalCodes.JudgmentFieldIncompatible, FailureReason.Refused),
        Spec(RefusalCodes.JudgmentOldFormat, FailureReason.Refused),
    ];
}
