using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsReport() =>
    [
        Spec(RefusalCodes.ReportAssessmentNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.ReportInvalidKind, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ReportRefused, FailureReason.Refused),
    ];
}
