using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsAgent() =>
    [
        Spec(RefusalCodes.AgentBuiltIn, FailureReason.Refused),
        Spec(RefusalCodes.AgentHuman, FailureReason.Refused),
        Spec(RefusalCodes.AgentHumanName, FailureReason.Refused),
        Spec(RefusalCodes.AgentIdentityConflict, FailureReason.Refused),
        Spec(RefusalCodes.AgentVersionConflict, FailureReason.Refused),
    ];
}
