using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsActivity() =>
    [
        Spec(RefusalCodes.ActivityLogUnreadable, FailureReason.NotFound),
    ];
}
