using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsCommand() =>
    [
        Spec(RefusalCodes.CommandAdvancedAiModeRequired, FailureReason.Refused),
        Spec(RefusalCodes.CommandAutonomousRequired, FailureReason.Refused),
        Spec(RefusalCodes.CommandDeveloperCommandsRequired, FailureReason.Refused),
    ];
}
