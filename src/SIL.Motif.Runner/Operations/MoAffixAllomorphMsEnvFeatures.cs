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
/// Names the <c>MoAffixAllomorph.MsEnvFeatures</c> create kind: the features an allomorph requires
/// on the stem it attaches to. Like <c>MoStemMsa.MsFeatures</c>, a fresh <c>FsFeatStruc</c> is already
/// valid when empty, so the create carries no payload and the feature values arrive through the
/// structure's own <c>createFeatureSpecs</c> operations.
/// </summary>
/// <remarks>
/// Unlike <c>LexEntry.LexemeForm</c>, a create into an occupied slot is refused rather than replacing the
/// incumbent, because the incumbent structure may already carry authored feature values.
/// </remarks>
public static class MoAffixAllomorphMsEnvFeaturesOperationKinds
{
    public const string CreateMsEnvFeatures = "grammar/moAffixAllomorph/createMsEnvFeatures";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(CreateMsEnvFeatures);
        OperationHandlerRegistry.Register(CreateMsEnvFeatures, MoAffixAllomorphMsEnvFeaturesCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(CreateMsEnvFeatures, SnapshotFields.MoAffixAllomorphMsEnvFeatures, true);
    }
#pragma warning restore CA2255
}

/// <summary>The <c>after</c> payload for <see cref="MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures"/>:
/// <c>{}</c>. Closed: any property is rejected.</summary>
public static class MoAffixAllomorphMsEnvFeaturesPayload
{
    private static readonly string[] AllowedProperties = Array.Empty<string>();

    public static void Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, AllowedProperties, kind);
    }
}

/// <summary>
/// Lowers <see cref="MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures"/>: creates an empty
/// <c>FsFeatStruc</c> with the authored identity and assigns it into the empty
/// <c>form.MsEnvFeaturesOA</c> slot. Must run inside an already-open unit of work.
/// </summary>
public static class MoAffixAllomorphMsEnvFeaturesCreateLowering
{
    public static IFsFeatStruc Apply(LcmCache cache, IMoAffixAllomorph form, Guid featStrucId)
    {
        if (form.MsEnvFeaturesOA is not null)
        {
            throw new InvalidOperationException(
                $"'{MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures}': the Required Features slot of " +
                $"allomorph '{form.Guid}' is occupied; creation cannot replace it.");
        }

        CreateIdentity.RequireFresh(cache, CanonicalId.FromGuid(featStrucId),
            MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures, "FsFeatStruc");
        var newFeatStruc = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create(featStrucId);
        form.MsEnvFeaturesOA = newFeatStruc;
        if (newFeatStruc.Guid != featStrucId || newFeatStruc.Owner?.Guid != form.Guid)
            throw new InvalidOperationException("Native MsEnvFeatures creation did not preserve its authored identity and owner.");
        return newFeatStruc;
    }
}

/// <summary>Resolves, snapshots, lowers, and re-snapshots one
/// <see cref="MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures"/> operation.</summary>
internal sealed class MoAffixAllomorphMsEnvFeaturesCreateHandler : IOperationHandler
{
    private const string Kind = MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures;
    internal static readonly MoAffixAllomorphMsEnvFeaturesCreateHandler Instance = new();
    private MoAffixAllomorphMsEnvFeaturesCreateHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        if (operation.Placement is not null)
            throw new InvalidOperationException($"'{Kind}': placement applies only to a sequence.");
        var entityId = RequireEntityId(operation);
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        MoAffixAllomorphMsEnvFeaturesPayload.Parse(after, Kind);

        var (id, form) = TargetResolution.Resolve<IMoAffixAllomorph>(cache, operation, Kind);
        touchedTargets.Add(id);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "FsFeatStruc");

        var before = ReferenceFieldSnapshotting.ReadAlternatives(form.MsEnvFeaturesOA);
        MoAffixAllomorphMsEnvFeaturesCreateLowering.Apply(cache, form, entityId.ToGuid());
        return new ExpectedEffect(id, SnapshotFields.MoAffixAllomorphMsEnvFeatures, before,
            ReferenceFieldSnapshotting.ReadAlternatives(form.MsEnvFeaturesOA));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = RequireEntityId(operation);
        var (id, form) = TargetResolution.Resolve<IMoAffixAllomorph>(cache, operation, Kind);
        var current = new Dictionary<string, string>(ReferenceFieldSnapshotting.ReadAlternatives(form.MsEnvFeaturesOA));
        CreateIdentity.AddCollision(cache, entityId, form.MsEnvFeaturesOA?.Guid, current);
        return new ExpectedEffect(id, SnapshotFields.MoAffixAllomorphMsEnvFeatures, current, current);
    }

    private static CanonicalId RequireEntityId(OperationEnvelope operation) =>
        operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'entityId'.");
}
