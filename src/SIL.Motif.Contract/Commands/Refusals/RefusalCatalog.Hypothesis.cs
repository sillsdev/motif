using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsHypothesis() =>
    [
        Spec(RefusalCodes.HypothesisInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.HypothesisNotFound, FailureReason.NotFound,
            Command("list hypotheses", Caller("project"))),
        Spec(RefusalCodes.HypothesisRule, FailureReason.Refused),
    ];
}
