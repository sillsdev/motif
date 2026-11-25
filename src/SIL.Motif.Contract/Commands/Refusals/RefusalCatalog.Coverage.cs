using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsCoverage() =>
    [
        Spec(RefusalCodes.CoverageInvalidRequest, FailureReason.InvalidArgument),
        Spec(RefusalCodes.CoverageFileUnreadable, FailureReason.NotFound),
        Spec(RefusalCodes.CoverageNotUtf8, FailureReason.Refused),
        Spec(RefusalCodes.CoverageEmptyFile, FailureReason.Refused),
        Spec(RefusalCodes.CoverageBadHeader, FailureReason.Refused),
        Spec(RefusalCodes.CoverageWrongFieldCount, FailureReason.Refused),
        Spec(RefusalCodes.CoverageEmptyWord, FailureReason.Refused),
        Spec(RefusalCodes.CoverageEmptyGrammarRevision, FailureReason.Refused),
        Spec(RefusalCodes.CoverageBadTokens, FailureReason.Refused),
        Spec(RefusalCodes.CoverageBadState, FailureReason.Refused),
        Spec(RefusalCodes.CoverageBadExpectedReadings, FailureReason.Refused),
        Spec(RefusalCodes.CoverageRowCountMismatch, FailureReason.Refused),
        Spec(RefusalCodes.CoverageTokenSumMismatch, FailureReason.Refused),
        Spec(RefusalCodes.CoverageDigestMismatch, FailureReason.Refused),
        Spec(RefusalCodes.CoverageSidecarMissing, FailureReason.Refused),
        Spec(RefusalCodes.CoverageSidecarInvalidJson, FailureReason.Refused),
        Spec(RefusalCodes.CoverageSidecarDuplicateKey, FailureReason.Refused),
        Spec(RefusalCodes.CoverageSidecarMissingKey, FailureReason.Refused),
        Spec(RefusalCodes.CoverageSidecarInvalidValue, FailureReason.Refused),
        Spec(RefusalCodes.CoverageFrequencyMalformed, FailureReason.Refused),
        Spec(RefusalCodes.CoverageCheckMalformed, FailureReason.Refused),
        Spec(RefusalCodes.CoverageCheckPinMismatch, FailureReason.Refused),
    ];
}
