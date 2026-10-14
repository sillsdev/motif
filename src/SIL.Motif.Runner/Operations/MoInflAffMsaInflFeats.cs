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
/// Names the <c>MoInflAffMsa.InflFeats</c> create kind: the inflection features an affix requires
/// on the word it attaches to. Like <c>MoStemMsa.MsFeatures</c>, a fresh <c>FsFeatStruc</c> is already
/// valid when empty, so the create carries no payload and the feature values arrive through the
/// structure's own <c>createFeatureSpecs</c> operations.
/// </summary>
/// <remarks>
/// Unlike <c>LexEntry.LexemeForm</c>, a create into an occupied slot is refused rather than replacing the
/// incumbent, because the incumbent structure may already carry authored feature values.
/// </remarks>
public static class MoInflAffMsaInflFeatsOperationKinds
{
    public const string CreateInflFeats = "grammar/moInflAffMsa/createInflFeats";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(CreateInflFeats);
        OperationHandlerRegistry.Register(CreateInflFeats, MoInflAffMsaInflFeatsCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(CreateInflFeats, SnapshotFields.MoInflAffMsaInflFeats, true);
    }
#pragma warning restore CA2255
}

/// <summary>The <c>after</c> payload for <see cref="MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats"/>:
/// <c>{}</c>. Closed: any property is rejected.</summary>
public static class MoInflAffMsaInflFeatsPayload
{
    private static readonly string[] AllowedProperties = Array.Empty<string>();

    public static void Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, AllowedProperties, kind);
    }
}

/// <summary>
/// Lowers <see cref="MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats"/>: creates an empty
/// <c>FsFeatStruc</c> with the authored identity and assigns it into the empty
/// <c>msa.InflFeatsOA</c> slot. Must run inside an already-open unit of work.
/// </summary>
public static class MoInflAffMsaInflFeatsCreateLowering
{
    public static IFsFeatStruc Apply(LcmCache cache, IMoInflAffMsa msa, Guid featStrucId)
    {
        if (msa.InflFeatsOA is not null)
        {
            throw new InvalidOperationException(
                $"'{MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats}': the inflection features slot of " +
                $"MSA '{msa.Guid}' is occupied; creation cannot replace it.");
        }

        CreateIdentity.RequireFresh(cache, CanonicalId.FromGuid(featStrucId),
            MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats, "FsFeatStruc");
        var newFeatStruc = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create(featStrucId);
        msa.InflFeatsOA = newFeatStruc;
        if (newFeatStruc.Guid != featStrucId || newFeatStruc.Owner?.Guid != msa.Guid)
            throw new InvalidOperationException("Native InflFeats creation did not preserve its authored identity and owner.");
        return newFeatStruc;
    }
}

/// <summary>Resolves, snapshots, lowers, and re-snapshots one
/// <see cref="MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats"/> operation.</summary>
internal sealed class MoInflAffMsaInflFeatsCreateHandler : IOperationHandler
{
    private const string Kind = MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats;
    internal static readonly MoInflAffMsaInflFeatsCreateHandler Instance = new();
    private MoInflAffMsaInflFeatsCreateHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        if (operation.Placement is not null)
            throw new InvalidOperationException($"'{Kind}': placement applies only to a sequence.");
        var entityId = RequireEntityId(operation);
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        MoInflAffMsaInflFeatsPayload.Parse(after, Kind);

        var (id, msa) = TargetResolution.Resolve<IMoInflAffMsa>(cache, operation, Kind);
        touchedTargets.Add(id);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "FsFeatStruc");

        var before = ReferenceFieldSnapshotting.ReadAlternatives(msa.InflFeatsOA);
        MoInflAffMsaInflFeatsCreateLowering.Apply(cache, msa, entityId.ToGuid());
        return new ExpectedEffect(id, SnapshotFields.MoInflAffMsaInflFeats, before,
            ReferenceFieldSnapshotting.ReadAlternatives(msa.InflFeatsOA));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = RequireEntityId(operation);
        var (id, msa) = TargetResolution.Resolve<IMoInflAffMsa>(cache, operation, Kind);
        var current = new Dictionary<string, string>(ReferenceFieldSnapshotting.ReadAlternatives(msa.InflFeatsOA));
        CreateIdentity.AddCollision(cache, entityId, msa.InflFeatsOA?.Guid, current);
        return new ExpectedEffect(id, SnapshotFields.MoInflAffMsaInflFeats, current, current);
    }

    private static CanonicalId RequireEntityId(OperationEnvelope operation) =>
        operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'entityId'.");
}
