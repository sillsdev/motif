using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRequest() =>
    [
        Spec(RefusalCodes.RequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.RequestNoRequest, FailureReason.Refused),
        Spec(RefusalCodes.RequestNotFound, FailureReason.NotFound,
            Command("list requests", Caller("project"))),
        Spec(RefusalCodes.RequestNotOpen, FailureReason.Refused,
            Command("list requests", Caller("project"))),
        Spec(RefusalCodes.RequestProposalOpen, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesRequest() =>
    [
        RefusalCodes.RequestInvalid,
        RefusalCodes.RequestNoRequest,
        RefusalCodes.RequestNotFound,
        RefusalCodes.RequestNotOpen,
    ];
}
