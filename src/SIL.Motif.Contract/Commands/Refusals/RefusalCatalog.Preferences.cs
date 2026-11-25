using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsPreferences() =>
    [
        Spec(RefusalCodes.PreferencesRefused, FailureReason.Refused),
        Spec(RefusalCodes.PreferencesUnavailable, FailureReason.Refused),
    ];
}
