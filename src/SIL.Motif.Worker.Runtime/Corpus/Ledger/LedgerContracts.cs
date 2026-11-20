namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>The five states a lane ledger records for a word type.</summary>
internal enum LedgerState { Expected, Unverified, Disposition, Open, Lost }

/// <summary>Why the adapter refused a ledger TSV, a sidecar, or a frequency list.</summary>
internal enum LedgerRefusalCode
{
    NotUtf8, EmptyFile, BadHeader, WrongFieldCount, EmptyWord, EmptyGrammarRevision, BadTokens, BadState,
    BadExpectedReadings, RowCountMismatch, TokenSumMismatch, DigestMismatch,
    SidecarMissing, SidecarInvalidJson, SidecarDuplicateKey, SidecarMissingKey, SidecarInvalidValue,
    FrequencyMalformed, CheckMalformed, CheckPinMismatch,
}

internal sealed class LedgerRefusalException(LedgerRefusalCode code, string message, int? row = null)
    : Exception(message)
{
    public LedgerRefusalCode Code { get; } = code;
    public int? Row { get; } = row;
}

/// <summary>One parsed signature of an <c>expected_readings</c> cell, before any native resolution.</summary>
internal sealed record ExpectedReadingItem(
    string? EntryId, string? SenseId, string? MsaId, IReadOnlyList<string> MorphIds,
    IReadOnlyList<string> Features, string Canonical);

/// <summary>A data row as written, with its file position kept for diagnostics.</summary>
internal sealed record LedgerRow(
    int RowNumber, string Word, long Tokens, LedgerState State, string StateText, string ExpectedReadingsText,
    IReadOnlyList<ExpectedReadingItem> ExpectedReadings, string? Class, string? Evidence, string? Provenance,
    string GrammarRevision);

internal sealed record LedgerFile(string Digest, IReadOnlyList<LedgerRow> Rows, long TokenSum);

internal sealed record LedgerSidecar(
    string Digest, string TokenizerId, string TokenizerSha256, string Fold, string Normalization,
    IReadOnlyList<string> SourceIds, string ScopeFilter, string FrequencySource, string GrammarRevision,
    long RowCount, long TokenSum);
