using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Runner.Composers;
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the create kind for <c>LexEntry.AlternateForms</c>, the entry's ordered further forms. The
/// move kind for the same sequence is <see cref="LexEntryAlternateFormsOperationKinds.MoveAlternateForms"/>.
/// </summary>
/// <remarks>
/// The payload is the same closed <c>{ morphType, ws, text }</c> shape as
/// <see cref="LexEntryLexemeFormOperationKinds.CreateLexemeForm"/>, and the morph type picks the concrete
/// class the same way. Unlike a lexeme form, a new alternate joins an ordered sequence, so a nonempty
/// sequence requires identity-relative placement, and no incumbent form is ever displaced.
/// </remarks>
public static class LexEntryAlternateFormsCreateOperationKinds
{
    public const string CreateAlternateForms = "lexical/lexEntry/createAlternateForms";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(CreateAlternateForms);
        OperationHandlerRegistry.Register(CreateAlternateForms, LexEntryAlternateFormsCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(CreateAlternateForms, SnapshotFields.LexEntryAlternateForms, false);
    }
#pragma warning restore CA2255
}

/// <summary>The <c>after</c> payload for <see cref="LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms"/>:
/// <c>{ "morphType": "&lt;canonicalId&gt;", "ws": "...", "text": "..." }</c>. Closed: any other property is
/// rejected, and all three are required. A second variant, <c>{ "class": "MoAffixProcess", "morphType": "..." }</c>,
/// adds an empty affix process whose input and output are built by later operations.</summary>
public static class LexEntryAlternateFormsCreatePayload
{
    private static readonly string[] AllowedProperties = { "morphType", "ws", "text" };

    /// <summary>True when the payload is the affix-process variant, <c>{ "class": "MoAffixProcess", "morphType" }</c>.</summary>
    public static bool IsProcess(JsonElement after) =>
        after.ValueKind == JsonValueKind.Object && after.TryGetProperty("class", out _);

    public static (CanonicalId MorphType, string Ws, string Text) Parse(JsonElement after)
    {
        const string kind = LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms;
        SoundSystemIntentParsing.Object(after, kind, AllowedProperties);
        return (ClosedPayloadParsing.GetRequiredCanonicalId(after, "morphType", kind),
            ClosedPayloadParsing.GetRequiredString(after, "ws", kind),
            ClosedPayloadParsing.GetRequiredString(after, "text", kind));
    }
}

/// <summary>
/// Lowers <see cref="LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms"/>: builds a concrete
/// <c>MoStemAllomorph</c> or <c>MoAffixAllomorph</c> with the authored identity, morph type and written
/// form, and inserts it into <c>entry.AlternateFormsOS</c> at the index the placement names.
/// Must run inside an already-open unit of work.
/// </summary>
public static class LexEntryAlternateFormsCreateLowering
{
    public static IMoForm Apply(LcmCache cache, ILexEntry entry, Guid formId, CanonicalId morphTypeId,
        string writingSystemTag, string text, int index)
    {
        const string kind = LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms;
        var morphType = ReferenceFieldLowering.Resolve<IMoMorphType>(cache, morphTypeId, kind);
        var concreteClass = MoFormConcreteClassSelection.ClassFor(morphType, kind);
        LexicalFormAuthoring.RequireMatchingKind(entry, concreteClass, kind);
        var newForm = MoFormConcreteClassSelection.Create(cache, concreteClass, formId);
        entry.AlternateFormsOS.Insert(index, newForm);
        newForm.MorphTypeRA = morphType;
        MultiAlternativesFieldLowering.ApplySet(cache, newForm.Form, writingSystemTag, text, kind);
        return newForm;
    }
}

/// <summary>Resolves, places, lowers, and re-snapshots one
/// <see cref="LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms"/> operation.</summary>
internal sealed class LexEntryAlternateFormsCreateHandler : IOperationHandler
{
    private const string Kind = LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms;
    internal static readonly LexEntryAlternateFormsCreateHandler Instance = new();
    private LexEntryAlternateFormsCreateHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'entityId'.");
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        if (LexEntryAlternateFormsCreatePayload.IsProcess(after))
        {
            var processMembers = entry.AlternateFormsOS.Cast<ICmObject>().ToArray();
            var processIndex = OwningCreateHandler<ILexEntry>.InsertionIndex(Kind, processMembers, operation.Placement);
            touchedTargets.Add(id);
            var processBefore = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
            AffixProcessConstruction.ApplyAlternate(cache, entry, entityId, after, processIndex);
            return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, processBefore,
                ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS));
        }
        var (morphTypeId, ws, text) = LexEntryAlternateFormsCreatePayload.Parse(after);
        var morphType = ReferenceFieldLowering.Resolve<IMoMorphType>(cache, morphTypeId, Kind);
        var concreteClass = MoFormConcreteClassSelection.ClassFor(morphType, Kind);
        LexicalFormAuthoring.RequireMatchingKind(entry, concreteClass, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind,
            concreteClass == MoFormConcreteClass.StemAllomorph ? "MoStemAllomorph" : "MoAffixAllomorph");
        var members = entry.AlternateFormsOS.Cast<ICmObject>().ToArray();
        var index = OwningCreateHandler<ILexEntry>.InsertionIndex(Kind, members, operation.Placement);
        touchedTargets.Add(id);

        var before = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        LexEntryAlternateFormsCreateLowering.Apply(cache, entry, entityId.ToGuid(), morphTypeId, ws, text, index);
        return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, before,
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'entityId'.");
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        var current = new Dictionary<string, string>(ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS));
        if (!entry.AlternateFormsOS.Any(form => form.Guid == entityId.ToGuid()))
            CreateIdentity.AddCollision(cache, entityId, null, current);
        return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, current, current);
    }
}
