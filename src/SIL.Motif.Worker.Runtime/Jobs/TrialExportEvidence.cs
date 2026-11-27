using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Worker.Jobs;

internal sealed record TrialExportProvenance(string? BuildSha, string ProposalId, string ProposalRevision,
    TrialExportIntent Intent, BaselineToken Baseline, string? BaselineSourceSha256, string? BaselineSidecarSha256,
    TrialExportParserIdentity PanGlossVersion, TrialExportParserIdentity PanGlossExecutableSha256,
    TrialGateDeclaration? GateDeclaration)
{
    [JsonPropertyOrder(-1)]
    public string? ProjectKey { get; init; }
}

internal sealed record TrialExportIntent(string Algorithm, string Digest);

internal sealed record TrialExportParserIdentity(string? Before, string? After);

internal sealed record TrialExportWord(string CaseKey, string? WritingSystemId, string SubmittedForm,
    IReadOnlyList<TrialExportMembership> Memberships, IReadOnlyList<TrialExportForm> ExpandedForms,
    TrialExportStatus Status, string ReasonCode, IReadOnlyList<TrialExportCause> ParserCauses)
{
    public ExplicitTrialInput? ExplicitInput { get; init; }
}

internal sealed record TrialExportMembership(string SetId, string CaseId, string Role);

internal sealed record TrialExportForm(string Form, string FormKey, string ExpansionKind, TrialExportStatus Status,
    string ReasonCode, IReadOnlyList<TrialExportCause> ParserCauses, TrialExportSide Before, TrialExportSide After,
    IReadOnlyList<string> LostReadingIds);

internal sealed record TrialExportSide(string? Outcome, bool Dispatched, bool Complete, TrialExportStatus Status,
    string ReasonCode, IReadOnlyList<TrialExportCause> ParserCauses, IReadOnlyList<TrialExportReading> Readings);

internal sealed record TrialExportCause(string Code, IReadOnlyList<string> Details);

internal sealed record TrialExportInterruption(string Side, TrialExportCause Cause);

internal sealed record TrialExportReading(string Id, ReadingSignature Signature, ReadingAcceptance Acceptance,
    IReadOnlyList<ReadingAcceptance> MorphemeAcceptance);

[JsonConverter(typeof(JsonStringEnumConverter<TrialExportStatus>))]
internal enum TrialExportStatus
{
    [JsonStringEnumMemberName("accepted")] Accepted,
    [JsonStringEnumMemberName("skipped")] Skipped,
    [JsonStringEnumMemberName("lost")] Lost,
    [JsonStringEnumMemberName("partial")] Partial,
    [JsonStringEnumMemberName("stopped")] Stopped,
    [JsonStringEnumMemberName("invalid-evidence")] InvalidEvidence,
    [JsonStringEnumMemberName("capped")] Capped,
    [JsonStringEnumMemberName("timeout")] Timeout,
    [JsonStringEnumMemberName("killed")] Killed,
}
