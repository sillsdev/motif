using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsComparison() =>
    [
        Spec(RefusalCodes.ComparisonAssessmentNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.ComparisonRefused, FailureReason.Refused),
    ];
}
