namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A coverage request names no ledger, sidecar or frequency file, or gives a limit or frequency format outside its range.</summary>
    public const string CoverageInvalidRequest = "coverage.invalid-request";
    /// <summary>A named ledger, sidecar, frequency, audit or protected-set path cannot be read.</summary>
    public const string CoverageFileUnreadable = "coverage.file-unreadable";
    public const string CoverageNotUtf8 = "coverage.not-utf8";
    public const string CoverageEmptyFile = "coverage.empty-file";
    public const string CoverageBadHeader = "coverage.bad-header";
    public const string CoverageWrongFieldCount = "coverage.wrong-field-count";
    public const string CoverageEmptyWord = "coverage.empty-word";
    public const string CoverageEmptyGrammarRevision = "coverage.empty-grammar-revision";
    public const string CoverageBadTokens = "coverage.bad-tokens";
    public const string CoverageBadState = "coverage.bad-state";
    public const string CoverageBadExpectedReadings = "coverage.bad-expected-readings";
    public const string CoverageRowCountMismatch = "coverage.row-count-mismatch";
    public const string CoverageTokenSumMismatch = "coverage.token-sum-mismatch";
    public const string CoverageDigestMismatch = "coverage.digest-mismatch";
    public const string CoverageSidecarMissing = "coverage.sidecar-missing";
    public const string CoverageSidecarInvalidJson = "coverage.sidecar-invalid-json";
    public const string CoverageSidecarDuplicateKey = "coverage.sidecar-duplicate-key";
    public const string CoverageSidecarMissingKey = "coverage.sidecar-missing-key";
    public const string CoverageSidecarInvalidValue = "coverage.sidecar-invalid-value";
    public const string CoverageFrequencyMalformed = "coverage.frequency-malformed";
    public const string CoverageCheckMalformed = "coverage.check-malformed";
    public const string CoverageCheckPinMismatch = "coverage.check-pin-mismatch";
}
