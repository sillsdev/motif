using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the two <c>MoDerivAffMsa</c> feature-structure create kinds. FromMsFeatures is what the stem must carry for
/// the affix to apply; ToMsFeatures is what the derived stem carries afterwards. The parser overlays ToMsFeatures on
/// the stem's features with priority, so a value it names replaces the stem's value for that feature.
/// A fresh <c>FsFeatStruc</c> is valid when empty, so the create carries no payload; values arrive through the
/// structure's own <c>createFeatureSpecs</c> operations. An occupied slot is refused, never replaced.
/// </summary>
public static class MoDerivAffMsaMsFeaturesOperationKinds
{
    public const string CreateFromMsFeatures = "grammar/moDerivAffMsa/createFromMsFeatures";
    public const string CreateToMsFeatures = "grammar/moDerivAffMsa/createToMsFeatures";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(CreateFromMsFeatures);
        OperationKindRegistry.Register(CreateToMsFeatures);
        OperationHandlerRegistry.Register(CreateFromMsFeatures, new MoDerivAffMsaMsFeaturesCreateHandler(CreateFromMsFeatures, false));
        OperationHandlerRegistry.Register(CreateToMsFeatures, new MoDerivAffMsaMsFeaturesCreateHandler(CreateToMsFeatures, true));
        OperationSlotRegistry.RegisterOwningCreate(CreateFromMsFeatures, SnapshotFields.MoDerivAffMsaFromMsFeatures, true);
        OperationSlotRegistry.RegisterOwningCreate(CreateToMsFeatures, SnapshotFields.MoDerivAffMsaToMsFeatures, true);
    }
#pragma warning restore CA2255
}

/// <summary>The <c>after</c> payload for both derivational create kinds: <c>{}</c>. Closed: any property is rejected.</summary>
public static class MoDerivAffMsaMsFeaturesPayload
{
    public static void Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, Array.Empty<string>(), kind);
    }
}

/// <summary>Resolves, snapshots, lowers and re-snapshots one create of a derivational affix's From or To feature structure.</summary>
internal sealed class MoDerivAffMsaMsFeaturesCreateHandler(string kind, bool toSide) : IOperationHandler
{
    private string Field => toSide ? SnapshotFields.MoDerivAffMsaToMsFeatures : SnapshotFields.MoDerivAffMsaFromMsFeatures;
    private IFsFeatStruc? Slot(IMoDerivAffMsa msa) => toSide ? msa.ToMsFeaturesOA : msa.FromMsFeaturesOA;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{kind}': storage GUID overrides are not supported for this create.");
        if (operation.Placement is not null)
            throw new InvalidOperationException($"'{kind}': placement applies only to a sequence.");
        var entityId = RequireEntityId(operation);
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        MoDerivAffMsaMsFeaturesPayload.Parse(after, kind);

        var (id, msa) = TargetResolution.Resolve<IMoDerivAffMsa>(cache, operation, kind);
        touchedTargets.Add(id);
        if (Slot(msa) is not null)
            throw new InvalidOperationException(
                $"'{kind}': the feature structure slot of MSA '{msa.Guid}' is occupied; creation cannot replace it.");
        CreateIdentity.RequireFresh(cache, entityId, kind, "FsFeatStruc");

        var before = ReferenceFieldSnapshotting.ReadAlternatives(Slot(msa));
        var created = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create(entityId.ToGuid());
        if (toSide) msa.ToMsFeaturesOA = created; else msa.FromMsFeaturesOA = created;
        if (created.Guid != entityId.ToGuid() || created.Owner?.Guid != msa.Guid)
            throw new InvalidOperationException("Native feature structure creation did not preserve its authored identity and owner.");
        return new ExpectedEffect(id, Field, before, ReferenceFieldSnapshotting.ReadAlternatives(Slot(msa)));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = RequireEntityId(operation);
        var (id, msa) = TargetResolution.Resolve<IMoDerivAffMsa>(cache, operation, kind);
        var current = new Dictionary<string, string>(ReferenceFieldSnapshotting.ReadAlternatives(Slot(msa)));
        CreateIdentity.AddCollision(cache, entityId, Slot(msa)?.Guid, current);
        return new ExpectedEffect(id, Field, current, current);
    }

    private CanonicalId RequireEntityId(OperationEnvelope operation) =>
        operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'entityId'.");
}
