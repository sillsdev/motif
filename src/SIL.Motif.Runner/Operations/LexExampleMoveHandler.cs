using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Moves an example sentence from one sense to another by changing its owner, as LibLCM allows: the example keeps its
/// identity, text, reference and translations. Anything outside the example that still refers to it is refused,
/// because what such a reference means may depend on the sense that owns the example and nothing here can tell.
/// </summary>
internal sealed class LexExampleMoveHandler : ICompoundOperationHandler
{
    internal static readonly LexExampleMoveHandler Instance = new();
    private const string Kind = LexExampleOperationKinds.Move;

    private LexExampleMoveHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (exampleId, example, from, payload) = ResolveSource(cache, operation);
        var to = ResolveTarget(cache, payload, from);
        RequireMovable(example);
        var members = to.ExamplesOS.Cast<ICmObject>().ToArray();
        var index = OwningCreateHandler<ILexSense>.InsertionIndex(Kind, members, operation.Placement);
        var fromBefore = ExampleViews.Examples(from);
        var toBefore = ExampleViews.Examples(to);
        var preview = Preview(example, from, to, payload, operation.Placement);

        to.ExamplesOS.Insert(index, example);
        touchedTargets.Add(exampleId);
        touchedTargets.Add(payload.FromSense);
        touchedTargets.Add(payload.ToSense);
        return
        [
            new(payload.FromSense, SnapshotFields.LexSenseExamples, fromBefore, ExampleViews.Examples(from), preview),
            new(payload.ToSense, SnapshotFields.LexSenseExamples, toBefore, ExampleViews.Examples(to), preview),
        ];
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    /// <summary>
    /// Reads only what exists now. The target sense may be created by an earlier operation of the Draft, so it counts
    /// as having no examples until it exists; Apply validates it.
    /// </summary>
    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (_, example, from, payload) = ResolveSource(cache, operation);
        var fromExamples = ExampleViews.Examples(from);
        var to = cache.ServiceLocator.ObjectRepository.TryGetObject(payload.ToSense.ToGuid(), out var found) ? found as ILexSense : null;
        var toExamples = to is null ? new Dictionary<string, string>() : ExampleViews.Examples(to);
        var preview = Preview(example, from, to, payload, operation.Placement);
        return
        [
            new(payload.FromSense, SnapshotFields.LexSenseExamples, fromExamples, fromExamples, preview),
            new(payload.ToSense, SnapshotFields.LexSenseExamples, toExamples, toExamples),
        ];
    }

    private static (CanonicalId Id, ILexExampleSentence Example, ILexSense From, LexExampleMovePayload Payload) ResolveSource(
        LcmCache cache, OperationEnvelope operation)
    {
        var payload = LexExampleMovePayload.Parse(operation.After ??
            throw new InvalidOperationException($"'{Kind}' operation requires 'after'."));
        var (id, example) = TargetResolution.Resolve<ILexExampleSentence>(cache, operation, Kind);
        if (example.Owner is not ILexSense from || from.Guid != payload.FromSense.ToGuid())
            throw new InvalidOperationException(
                $"'{Kind}' names a sense the example no longer belongs to; recompute the Draft.");
        return (id, example, from, payload);
    }

    private static ILexSense ResolveTarget(LcmCache cache, LexExampleMovePayload payload, ILexSense from)
    {
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(payload.ToSense.ToGuid(), out var found) || found is not ILexSense to)
            throw new InvalidOperationException(
                $"'{Kind}' needs the target sense '{payload.ToSense.Value}' to exist; create it first.");
        if (to.Guid == from.Guid)
            throw new InvalidOperationException($"'{Kind}' moves an example to a different sense.");
        return to;
    }

    /// <summary>
    /// Throws when something outside the example refers to it. The example's own translations belong to it and move
    /// with it; any other referrer is named.
    /// </summary>
    internal static void RequireMovable(ILexExampleSentence example)
    {
        foreach (var referrer in example.ReferringObjects.Where(other => !IsInside(other, example)))
            throw new InvalidOperationException(
                $"'{Kind}' refuses {SenseRemoval.Describe(example)} because {SenseRemoval.Describe(referrer)} still refers to it; " +
                "remove or retarget that reference first.");
    }

    private static bool IsInside(ICmObject item, ILexExampleSentence example)
    {
        for (ICmObject? current = item; current is not null; current = current.Owner)
            if (current.Guid == example.Guid)
                return true;
        return false;
    }

    /// <summary>What the Dry Run shows: the whole example in every writing system, with both senses.</summary>
    private static JsonElement Preview(
        ILexExampleSentence example, ILexSense from, ILexSense? to, LexExampleMovePayload payload, Placement? placement)
    {
        var index = from.ExamplesOS.IndexOf(example);
        string? Neighbour(int at) => at >= 0 && at < from.ExamplesOS.Count
            ? CanonicalId.FromGuid(from.ExamplesOS[at].Guid).Value : null;
        return JsonSerializer.SerializeToElement(new
        {
            movedExample = RemovalPreview.Example(example),
            fromSense = new { sense = ExampleViews.Sense(from), index, after = Neighbour(index - 1), before = Neighbour(index + 1) },
            toSense = new
            {
                sense = to is null ? (object)new { id = payload.ToSense.Value } : ExampleViews.Sense(to),
                after = placement?.After?.Value,
                before = placement?.Before?.Value,
                position = placement is null ? "first example" : placement.After is not null ? "after the example in after" : "before the example in before",
            },
            reversal = "move the example back to the old sense at the recorded index; its identity and translations are unchanged",
        });
    }
}

/// <summary>Plain views of senses and their examples for effects and Dry Run previews.</summary>
internal static class ExampleViews
{
    /// <summary>A sense's examples by position, as an effect field reads them.</summary>
    internal static Dictionary<string, string> Examples(ILexSense sense)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < sense.ExamplesOS.Count; i++)
            result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = CanonicalId.FromGuid(sense.ExamplesOS[i].Guid).Value;
        return result;
    }

    /// <summary>A sense as a reviewer recognises it: its id, entry headword, nesting and gloss in every writing system.</summary>
    internal static object Sense(ILexSense sense) => new
    {
        id = CanonicalId.FromGuid(sense.Guid).Value,
        entry = SenseRemoval.EntryOf(sense) is { } entry ? new { id = CanonicalId.FromGuid(entry.Guid).Value, headword = entry.HeadWord?.Text } : null,
        kind = sense.Owner is ILexEntry ? "sense" : "sub-sense",
        gloss = RemovalPreview.AllWritingSystems(sense.Cache, sense.Gloss),
        definition = RemovalPreview.AllWritingSystems(sense.Cache, sense.Definition),
        examples = sense.ExamplesOS.Count,
    };
}
