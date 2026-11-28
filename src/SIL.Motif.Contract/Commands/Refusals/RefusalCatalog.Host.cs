using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsHost() =>
    [
        Spec(HostRefusalCodes.AdvancedAiDisabled, FailureReason.Refused),
        Spec(HostRefusalCodes.BaselineUnavailable, FailureReason.StoreInconsistent,
            Command("baseline capture", Caller("project"))),
        Spec(HostRefusalCodes.CliCancelled, FailureReason.Cancelled),
        Spec(HostRefusalCodes.CliOutOfMemory, FailureReason.Refused),
        Spec(HostRefusalCodes.CliStoreBusy, FailureReason.Busy,
            NextStep.Repeat()),
        Spec(HostRefusalCodes.CliUnexpected, FailureReason.StoreInconsistent),
        Spec(HostRefusalCodes.CliPickPath, FailureReason.InvalidArgument),
        Spec(HostRefusalCodes.GrammarUnknownKind, FailureReason.InvalidArgument),
        Spec(HostRefusalCodes.GuideUnknownTopic, FailureReason.InvalidArgument),
        Spec(HostRefusalCodes.ProjectRegistrationFailed, FailureReason.Refused,
            Command("open", Caller("path"))),
        Spec(HostRefusalCodes.ProjectUnknown, FailureReason.InvalidArgument),
        Spec(HostRefusalCodes.ToolCancelled, FailureReason.Cancelled),
        Spec(HostRefusalCodes.ToolOutOfMemory, FailureReason.Refused),
        Spec(HostRefusalCodes.ToolFailed, FailureReason.Busy,
            NextStep.Repeat()),
        Spec(HostRefusalCodes.ToolInvalidArgument, FailureReason.InvalidArgument),
        Spec(HostRefusalCodes.ToolMissingArgument, FailureReason.InvalidArgument,
            NextStep.Repeat()),
        Spec(HostRefusalCodes.ToolNotFound, FailureReason.NotFound),
        Spec(HostRefusalCodes.ToolUnknown, FailureReason.InvalidArgument),
        Spec(HostRefusalCodes.ToolUnknownArgument, FailureReason.InvalidArgument,
            NextStep.Repeat()),
    ];
}
