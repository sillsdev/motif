using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The <c>dry-run</c> report: the effects a Proposal would have, read back from a live baseline
/// without mutating it.
/// </summary>
public sealed record DryRunProjection(
    string ProposalId,
    string IntentDigest,
    string BaselineNote,
    IReadOnlyList<EffectView> Effects,
    string EffectDigest,
    string FootprintDigest)
{
    public SIL.Motif.Contract.Baselines.BaselineToken? Baseline { get; init; }

    public bool EvidenceCurrent { get; init; }

    /// <summary>The Proposal's operations in declared dependency order, with prerequisites named explicitly.</summary>
    public IReadOnlyList<ProposalOperationView> Operations { get; init; } = [];

    /// <summary>Warnings attached to the completed Dry Run.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The file containing the complete effects list, when one was written.</summary>
    [JsonIgnore]
    public string? EffectsFile { get; init; }

    /// <summary>The operation counts, warnings, project hash and effects-file path for this result.</summary>
    public OperationEffectSummary EffectsSummary => OperationEffectSummary.Create(
        Operations.Select(operation => operation.Kind), Effects.Count, Warnings,
        Baseline?.SemanticSnapshotDigest, effectsFile: EffectsFile);
}

/// <summary>Stable openings and phrases of Dry Run warnings that later readers, such as a Trial's stop report, look for.</summary>
public static class DryRunWarningMarkers
{
    /// <summary>Appears in the warning for a phonological rule whose alpha variables range over many feature values.</summary>
    public const string AlphaRuleExpansion = "alpha variable(s) over";
}
