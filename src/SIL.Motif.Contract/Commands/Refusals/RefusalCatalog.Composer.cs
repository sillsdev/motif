using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsComposer() =>
    [
        Spec(RefusalCodes.ComposerGoalOrSubject, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ComposerInvalidDetail, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ComposerUnknownSubject, FailureReason.InvalidArgument),
    ];
}
