using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsProject() =>
    [
        Spec(RefusalCodes.ProjectBackupUnreadable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProjectBusy, FailureReason.Busy,
            NextStep.Repeat()),
        Spec(RefusalCodes.ProjectStartupBusy, FailureReason.Busy,
            NextStep.Repeat()),
        Spec(RefusalCodes.ProjectInUse, FailureReason.Busy,
            NextStep.PersonStep()),
        Spec(RefusalCodes.ProjectInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ProjectNotFound, FailureReason.NotFound,
            NextStep.PersonStep()),
        Spec(RefusalCodes.ProjectOperationIo, FailureReason.Busy),
        Spec(RefusalCodes.ProjectStoreIo, FailureReason.Busy),
        Spec(RefusalCodes.ProjectTagInvalid, FailureReason.InvalidArgument,
            Command("project tag --set", Caller("project"))),
        Spec(RefusalCodes.ProjectTagTaken, FailureReason.Refused,
            Command("project tag --set", Caller("project"))),
        Spec(RefusalCodes.ProjectTagUnavailable, FailureReason.Refused,
            Command("project tag --set", Caller("project"))),
        Spec(RefusalCodes.ProjectUnloadable, FailureReason.Refused),
        Spec(RefusalCodes.ProjectWaitCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.ProjectRefused, FailureReason.Refused),
        Spec(RefusalCodes.ProjectSaving, FailureReason.Busy),
        Spec(RefusalCodes.ProjectUnreadable, FailureReason.Refused),
        Spec(RefusalCodes.ProjectWritingSystemMissing, FailureReason.Refused,
            NextStep.PersonStep()),
    ];

    private static IEnumerable<string> WindowCodesProject() =>
    [
        RefusalCodes.ProjectBackupUnreadable,
        RefusalCodes.ProjectBusy,
        RefusalCodes.ProjectStartupBusy,
        RefusalCodes.ProjectInUse,
        RefusalCodes.ProjectInvalid,
        RefusalCodes.ProjectNotFound,
        RefusalCodes.ProjectOperationIo,
        RefusalCodes.ProjectStoreIo,
        RefusalCodes.ProjectUnloadable,
        RefusalCodes.ProjectWaitCancelled,
    ];
}
