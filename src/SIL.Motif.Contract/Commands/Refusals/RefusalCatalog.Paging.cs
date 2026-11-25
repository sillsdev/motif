using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsPaging() =>
    [
        Spec(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument, NextStep.Repeat()),
    ];
}
