using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsProjectCreate() =>
    [
        Spec(RefusalCodes.ProjectCreateExists, FailureReason.Refused),
        Spec(RefusalCodes.ProjectCreateFailed, FailureReason.Refused),
        Spec(RefusalCodes.ProjectCreateFolderNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.ProjectCreateInvalidFolder, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProjectCreateInvalidName, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProjectCreateInvalidWordForming, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProjectCreateInvalidWritingSystem, FailureReason.InvalidArgument),
    ];
}
