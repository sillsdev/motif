using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRecordImport() =>
    [
        Spec(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportFieldUncovered, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportShapeUnsupported, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportValueStale, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportNotExecutable, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportChangeRefused, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportFileUnreadable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.RecordImportNothingToStage, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportCreateKindNotCreatable, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportCreatePartial, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportRecordGone, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportGuidInvalid, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportGuidPending, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportGuidPendingUnchecked, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportGuidRetired, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportCreateShape, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportCreateComputedField, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportCreateReservedField, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportChangedSincePreview, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportStrictRefused, FailureReason.Refused),
        Spec(RefusalCodes.RecordImportStrictNeedsDraft, FailureReason.InvalidArgument),
        Spec(RefusalCodes.RecordImportInvalidPage, FailureReason.InvalidArgument),
    ];
}
