using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsParse() =>
    [
        Spec(RefusalCodes.ParseAlreadyRunningHere, FailureReason.Busy),
        Spec(RefusalCodes.ParseAlreadyRunning, FailureReason.Busy,
            NextStep.Repeat()),
    ];

    private static IEnumerable<string> WindowCodesParse() =>
    [
        RefusalCodes.ParseAlreadyRunning,
        RefusalCodes.ParseAlreadyRunningHere,
    ];
}
