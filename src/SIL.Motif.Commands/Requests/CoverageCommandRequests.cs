namespace SIL.Motif.Commands.Requests;

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller; its Motif store supplies saved checks and word type dispositions when it exists.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="LedgerPath">The lane ledger TSV: the eight columns word, tokens, state, expected_readings, class, evidence, provenance, grammar_revision.</param>
/// <param name="SidecarPath">The ledger's sidecar JSON, which pins the tokenizer, fold, normalization, grammar revision, row count and token sum.</param>
/// <param name="FrequencyPath">The pinned frequency list: two tab-separated columns of word and count, or a JSON object of word to count.</param>
/// <param name="FrequencyFormat">tsv (the default; a .json file is read as json), tsv-header when the first line is a header, or json.</param>
/// <param name="AuditDirectory">A frozen audit sample directory; without it the Correct check is unmeasured.</param>
/// <param name="ProtectedSetPath">A protected-set file; without it the Stable check is unmeasured.</param>
/// <param name="Limit">The most recurring Open types to rank, from 1 to 200. The default is 50.</param>
/// <param name="Tokens">Also report the share of eligible tokens carried by Open singletons.</param>
public sealed record CoverageRequest(
    string FwDataPath, string ProductVersion, string LedgerPath, string SidecarPath, string FrequencyPath,
    string? FrequencyFormat = null, string? AuditDirectory = null, string? ProtectedSetPath = null, int Limit = 50,
    bool Tokens = false);
