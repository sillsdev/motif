using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// A composer declared once: its names, its closed <see cref="Shape"/>, its exposure, how an authored intent
/// parses into <typeparamref name="TIntent"/>, and how that intent lowers to operations. The engine runs it
/// through <see cref="IComposerDefinition"/> as shape check, then parse, then lower.
/// </summary>
/// <typeparam name="TIntent">The parsed intent the composer lowers.</typeparam>
internal abstract class ComposerDefinition<TIntent> : IComposerDefinition
{
    /// <inheritdoc />
    public abstract string Verb { get; }

    /// <inheritdoc />
    public abstract string Composer { get; }

    /// <inheritdoc cref="IComposerDefinition.Shape" />
    public abstract IntentShape Shape { get; }

    /// <inheritdoc cref="IComposerDefinition.Exposure" />
    public abstract ComposerExposure Exposure { get; }

    /// <inheritdoc />
    public virtual ReplacementRule? Replaces => null;

    /// <inheritdoc />
    public virtual string? PrincipalKind => null;

    /// <summary>Parses the authored intent, refusing a malformed value with the parser's own message.</summary>
    public abstract TIntent Parse(JsonElement authored);

    /// <summary>Lowers the parsed intent to the operations that author it.</summary>
    public abstract IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, TIntent intent);

    /// <summary>Existing objects the change affects, reported for review; null when it reports none.</summary>
    public virtual IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, TIntent intent) => null;

    /// <summary>Checks the additions against the operations already staged; the default accepts them.</summary>
    public virtual void ValidateAppend(
        IReadOnlyList<OperationEnvelope> staged, TIntent intent, IReadOnlyList<OperationEnvelope> additions)
    {
    }

    IntentShape? IComposerDefinition.Shape => Shape;

    ComposerExposure? IComposerDefinition.Exposure => Exposure;

    bool IComposerDefinition.CanReplacePrevious => Replaces is not null;

    IReadOnlyList<OperationEnvelope> IComposerDefinition.Compose(
        LcmCache cache, JsonElement authored, CanonicalId proposalId, Func<CanonicalId> mintId)
    {
        Shape.RequireClosed(authored, Composer);
        return Lower(new ComposeContext(cache, proposalId, mintId), Parse(authored));
    }

    void IComposerDefinition.ValidateAppend(
        IReadOnlyList<OperationEnvelope> staged, JsonElement authored, IReadOnlyList<OperationEnvelope> additions) =>
        ValidateAppend(staged, Parse(authored), additions);

    bool IComposerDefinition.ReplacePrevious(JsonElement authored, OperationEnvelope operation) =>
        Replaces?.Matches(authored, operation) ?? false;

    IReadOnlyList<ComposedRelatedObject>? IComposerDefinition.RelatedObjects(LcmCache cache, JsonElement authored) =>
        RelatedObjects(cache, Parse(authored));
}
