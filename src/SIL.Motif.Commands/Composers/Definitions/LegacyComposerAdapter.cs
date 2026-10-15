using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// Runs a composer still declared as a <see cref="DraftComposer"/> record through <see cref="IComposerDefinition"/>,
/// unchanged. It declares no shape, exposure or replacement rule, because the record's own delegates decide
/// those.
/// </summary>
internal sealed class LegacyComposerAdapter(DraftComposer legacy) : IComposerDefinition
{
    /// <summary>The wrapped record.</summary>
    public DraftComposer Legacy { get; } = legacy;

    /// <inheritdoc />
    public string Verb => Legacy.Verb;

    /// <inheritdoc />
    public string Composer => Legacy.Composer;

    /// <inheritdoc />
    public IntentShape? Shape => null;

    /// <inheritdoc />
    public ComposerExposure? Exposure => null;

    /// <inheritdoc />
    public ReplacementRule? Replaces => null;

    /// <inheritdoc />
    public string? PrincipalKind => null;

    /// <inheritdoc />
    public bool CanReplacePrevious => Legacy.ReplacePrevious is not null;

    /// <inheritdoc />
    public IReadOnlyList<OperationEnvelope> Compose(
        LcmCache cache, JsonElement authored, CanonicalId proposalId, Func<CanonicalId> mintId) =>
        Legacy.Build(cache, authored, proposalId);

    /// <inheritdoc />
    public void ValidateAppend(
        IReadOnlyList<OperationEnvelope> staged, JsonElement authored, IReadOnlyList<OperationEnvelope> additions) =>
        Legacy.ValidateAppend?.Invoke(staged, authored, additions);

    /// <inheritdoc />
    public bool ReplacePrevious(JsonElement authored, OperationEnvelope operation) =>
        Legacy.ReplacePrevious?.Invoke(authored, operation) ?? false;

    /// <inheritdoc />
    public IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, JsonElement authored) =>
        Legacy.RelatedObjects?.Invoke(cache, authored);
}
