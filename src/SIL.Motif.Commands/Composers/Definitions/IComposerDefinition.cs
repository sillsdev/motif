using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// One composer a Draft is authored through, as the compose engine sees it. The single verb and the batch both
/// run a composer through this interface, so neither has a path the other lacks.
/// </summary>
internal interface IComposerDefinition
{
    /// <summary>The CLI verb, and the <c>verb</c> a batch entry names.</summary>
    string Verb { get; }

    /// <summary>The composer name recorded in the Draft's composer provenance.</summary>
    string Composer { get; }

    /// <summary>The closed set of properties the intent may carry, or null when the composer checks them itself.</summary>
    IntentShape? Shape { get; }

    /// <summary>Where the composer is offered, or null when its callers declare that themselves.</summary>
    ComposerExposure? Exposure { get; }

    /// <summary>Which earlier edits in the Draft an intent replaces, or null when it replaces none by this rule.</summary>
    ReplacementRule? Replaces { get; }

    /// <summary>The kind of principal entity the intent creates, which a label may name, or null.</summary>
    string? PrincipalKind { get; }

    /// <summary>Whether <see cref="ReplacePrevious"/> can ever answer true.</summary>
    bool CanReplacePrevious { get; }

    /// <summary>Resolves the authored intent against the project to the operations it lowers to.</summary>
    IReadOnlyList<OperationEnvelope> Compose(
        LcmCache cache, JsonElement authored, CanonicalId proposalId, Func<CanonicalId> mintId);

    /// <summary>
    /// Checks the additions against the operations already staged: the Draft's own, then those added earlier in
    /// the same batch. Throws to refuse.
    /// </summary>
    void ValidateAppend(
        IReadOnlyList<OperationEnvelope> staged, JsonElement authored, IReadOnlyList<OperationEnvelope> additions);

    /// <summary>Whether an operation already in the Draft is an earlier edit this intent replaces in place.</summary>
    bool ReplacePrevious(JsonElement authored, OperationEnvelope operation);

    /// <summary>Existing objects the change affects, reported for review; null when the composer reports none.</summary>
    IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, JsonElement authored);
}
