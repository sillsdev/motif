using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsFsNotation() =>
    [
        Spec(RefusalCodes.FsNotationSyntax, FailureReason.Refused),
        Spec(RefusalCodes.FsNotationUnknownFeature, FailureReason.Refused),
        Spec(RefusalCodes.FsNotationUnknownValue, FailureReason.Refused),
        Spec(RefusalCodes.FsNotationAmbiguousLabel, FailureReason.Refused),
        Spec(RefusalCodes.FsNotationRepeatedFeature, FailureReason.Refused),
        Spec(RefusalCodes.FsNotationUnsupportedFeature, FailureReason.Refused),
        Spec(RefusalCodes.FsNotationUnrenderable, FailureReason.Refused),
    ];
}
