using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsCompoundRule() =>
    [
        Spec(RefusalCodes.CompoundRuleInvalidCategory, FailureReason.Refused),
        Spec(RefusalCodes.CompoundRuleDuplicate, FailureReason.Refused),
        Spec(RefusalCodes.CompoundRuleNoMorphologyData, FailureReason.Refused),
    ];
}
