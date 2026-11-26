using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWorker() =>
    [
        Spec(RefusalCodes.WorkerStopFailed, FailureReason.Refused,
            NextStep.PersonStep()),
    ];
}
