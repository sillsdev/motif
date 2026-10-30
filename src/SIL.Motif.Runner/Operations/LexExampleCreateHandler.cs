using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Adds a new example sentence, with its translations, to a sense. The sense may be created by an earlier operation
/// of the same Draft, so it is resolved when the operation is applied; the footprint reads only what exists now.
/// </summary>
internal sealed class LexExampleCreateHandler : IOperationHandler
{
    internal static readonly LexExampleCreateHandler Instance = new();
    private const string Kind = LexExampleOperationKinds.Create;

    private LexExampleCreateHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'entityId'.");
        var payload = LexExampleCreatePayload.Parse(operation.After ??
            throw new InvalidOperationException($"'{Kind}' operation requires 'after'."));
        var (senseId, sense) = TargetResolution.Resolve<ILexSense>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "LexExampleSentence");
        foreach (var translation in payload.Translations)
        {
            CreateIdentity.RequireFresh(cache, translation.Id, Kind, "CmTranslation");
            if (translation.Id.ToGuid() == entityId.ToGuid())
                throw new InvalidOperationException($"'{Kind}': a translation reuses the example's identity.");
        }
        var members = sense.ExamplesOS.Cast<ICmObject>().ToArray();
        var index = OwningCreateHandler<ILexSense>.InsertionIndex(Kind, members, operation.Placement);
        var before = ExampleViews.Examples(sense);

        var example = cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create(entityId.ToGuid());
        sense.ExamplesOS.Insert(index, example);
        ExampleForms.Apply(cache, example.Example, payload.Text, Kind);
        if (payload.Reference is { } reference)
            example.Reference = ExampleForms.Reference(cache, reference);
        var translations = cache.ServiceLocator.GetInstance<ICmTranslationFactory>();
        foreach (var translation in payload.Translations)
        {
            var created = translations.Create(example, ExampleForms.TypeOf(cache, translation.Type, Kind), translation.Id.ToGuid());
            ExampleForms.Apply(cache, created.Translation, translation.Text, Kind);
        }
        touchedTargets.Add(senseId);
        touchedTargets.Add(entityId);
        return new ExpectedEffect(senseId, SnapshotFields.LexSenseExamples, before, ExampleViews.Examples(sense),
            Preview(sense, entityId, payload));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'entityId'.");
        var payload = LexExampleCreatePayload.Parse(operation.After ??
            throw new InvalidOperationException($"'{Kind}' operation requires 'after'."));
        var (senseId, sense) = TargetResolution.Resolve<ILexSense>(cache, operation, Kind);
        var current = ExampleViews.Examples(sense);
        if (!sense.ExamplesOS.Any(item => item.Guid == entityId.ToGuid()))
            CreateIdentity.AddCollision(cache, entityId, null, current);
        return new ExpectedEffect(senseId, SnapshotFields.LexSenseExamples, current, current,
            Preview(sense, entityId, payload));
    }

    /// <summary>The Dry Run view: the example in every writing system, and the sense it joins.</summary>
    private static JsonElement Preview(ILexSense sense, CanonicalId example, LexExampleCreatePayload payload) =>
        JsonSerializer.SerializeToElement(new
        {
            addedExample = new
            {
                id = example.Value,
                text = ExampleForms.View(payload.Text),
                reference = payload.Reference,
                translations = payload.Translations.Select(translation => new
                {
                    id = translation.Id.Value,
                    type = translation.Type,
                    text = ExampleForms.View(translation.Text),
                }).ToArray(),
            },
            toSense = ExampleViews.Sense(sense),
            reversal = "delete the example and its translations by the ids recorded here",
        });
}
