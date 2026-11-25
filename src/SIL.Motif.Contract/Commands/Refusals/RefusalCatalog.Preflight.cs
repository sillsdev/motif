using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsPreflight() =>
    [
        Spec(RefusalCodes.PreflightUnavailable, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesPreflight() =>
    [
        RefusalCodes.PreflightUnavailable,
    ];
}
