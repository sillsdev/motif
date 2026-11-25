using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsAffixMaxApps() =>
    [
        Spec(RefusalCodes.AffixMaxAppsInvalidCount, FailureReason.Refused),
        Spec(RefusalCodes.AffixMaxAppsNotAffixMsa, FailureReason.Refused),
        Spec(RefusalCodes.AffixMaxAppsStale, FailureReason.Refused),
    ];
}
