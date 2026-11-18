using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The applied-change-log entry an Apply just wrote (or found already present), for display.</summary>
public sealed record AppliedLogEntrySummary(
    string ProposalId, string TimestampUtc, string User, string IntentDigest,
    string Mode = "person", string Message = "");

/// <summary>
/// The <c>apply</c> report: whether the Proposal actually mutated the project (idempotent no-op
/// otherwise), the resulting effects, and the applied-change-log entry it is recorded under. Cited operation rows
/// are read from the Applied Proposal's provenance and are absent when no operation cites a source, pinned by
/// <c>Citations_ListsEveryAppliedOperationWithItsSource_AndCanListOnlyTheUncitedOnes</c>.
/// </summary>
public sealed record ApplyProjection(
    string ProposalId,
    bool AlreadyApplied,
    string ResultNote,
    IReadOnlyList<EffectView> Effects,
    string EffectDigest,
    AppliedLogEntrySummary AppliedLogEntry,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReadinessProjection? Readiness = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CitedOperationRow>? Citations = null)
{
    /// <summary>The operation kinds in the applied Proposal.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> OperationKinds { get; init; } = [];

    /// <summary>Warnings reported with the applied effects.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The file containing the complete effects list, when one was written.</summary>
    [JsonIgnore]
    public string? EffectsFile { get; init; }

    /// <summary>The operation counts, warning previews and effects-file path for this result.</summary>
    public OperationEffectSummary EffectsSummary => OperationEffectSummary.Create(
        OperationKinds, Effects.Count, Warnings, effectsFile: EffectsFile);
}
