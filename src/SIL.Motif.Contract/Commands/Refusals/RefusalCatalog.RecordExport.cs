using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRecordExport() =>
    [
        Spec(RefusalCodes.RecordExportInvalidRequest, FailureReason.InvalidArgument),
        Spec(RefusalCodes.RecordExportNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.RecordExportOutputUnwritable, FailureReason.InvalidArgument),
    ];
}
