using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsGroup() =>
    [
        Spec(RefusalCodes.GroupRequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GroupAsOfUnavailable, FailureReason.Refused),
    ];
}
