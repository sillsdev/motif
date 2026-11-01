using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the two operations that point a non-sense user of a grammatical info (an ad hoc morpheme prohibition, a
/// parser analysis's morph bundle) at another grammatical info, in place and keeping its position. LibLCM holds those
/// references to any grammatical info, so changing an affix's kind moves them rather than refusing.
/// </summary>
public static class MsaReferenceRetargetOperationKinds
{
    /// <summary>Replaces one grammatical info with another in a morpheme prohibition's first and rest members.</summary>
    public const string RetargetProhibitionMorphemes = "grammar/moMorphAdhocProhib/retargetMorphemes";

    /// <summary>Points a morph bundle at another grammatical info.</summary>
    public const string SetBundleMsa = "analysis/wfiMorphBundle/setMsa";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(RetargetProhibitionMorphemes);
        OperationKindRegistry.Register(SetBundleMsa);
        OperationHandlerRegistry.Register(RetargetProhibitionMorphemes, ProhibitionMorphemesHandler.Instance);
        OperationHandlerRegistry.Register(SetBundleMsa, BundleMsaHandler.Instance);
        OperationSlotRegistry.RegisterMany(RetargetProhibitionMorphemes, operation =>
        {
            var target = OperationSlotRegistry.Target(operation);
            var payload = MsaReferenceRetargetPayload.Parse(OperationSlotRegistry.After(operation), RetargetProhibitionMorphemes);
            return
            [
                new OperationSlot(target, SnapshotFields.MoMorphAdhocProhibFirstMorpheme),
                new OperationSlot(target, SnapshotFields.MoMorphAdhocProhibRestOfMorphs,
                    OperationSlotRegistry.MemberKey(payload.From)),
                new OperationSlot(target, SnapshotFields.MoMorphAdhocProhibRestOfMorphs,
                    OperationSlotRegistry.MemberKey(payload.To)),
            ];
        });
        OperationSlotRegistry.Register(SetBundleMsa,
            operation => new OperationSlot(OperationSlotRegistry.Target(operation), SnapshotFields.WfiMorphBundleMsa));
    }
#pragma warning restore CA2255
}

/// <summary>The closed <c>after</c> payload: the grammatical info to leave and the one to point at.</summary>
public sealed record MsaReferenceRetargetPayload(CanonicalId From, CanonicalId To)
{
    /// <summary>Reads the payload of an operation.</summary>
    public static MsaReferenceRetargetPayload Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["from", "to"], kind);
        var from = ClosedPayloadParsing.GetRequiredCanonicalId(after, "from", kind);
        var to = ClosedPayloadParsing.GetRequiredCanonicalId(after, "to", kind);
        if (from.ToGuid() == to.ToGuid())
            throw new InvalidOperationException($"'{kind}': 'from' and 'to' must differ.");
        return new MsaReferenceRetargetPayload(from, to);
    }
}

internal sealed class ProhibitionMorphemesHandler : ICompoundOperationHandler
{
    internal static readonly ProhibitionMorphemesHandler Instance = new();
    private const string Kind = MsaReferenceRetargetOperationKinds.RetargetProhibitionMorphemes;
    private ProhibitionMorphemesHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = MsaReferenceRetargetPayload.Parse(OperationSlotRegistry.After(operation), Kind);
        var (id, rule) = TargetResolution.Resolve<IMoMorphAdhocProhib>(cache, operation, Kind);
        touchedTargets.Add(id);
        var to = ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, payload.To, Kind);
        var fromGuid = payload.From.ToGuid();
        if (rule.FirstMorphemeRA?.Guid != fromGuid && rule.RestOfMorphsRS.All(item => item.Guid != fromGuid))
            throw new InvalidOperationException(
                $"'{Kind}': the prohibition no longer names '{payload.From.Value}'; recompute the Draft.");
        var firstBefore = ReferenceFieldSnapshotting.ReadAlternatives(rule.FirstMorphemeRA);
        var restBefore = ReferenceSequenceFieldSnapshotting.ReadAlternatives(rule.RestOfMorphsRS);
        if (rule.FirstMorphemeRA?.Guid == fromGuid) rule.FirstMorphemeRA = to;
        for (var index = 0; index < rule.RestOfMorphsRS.Count; index++)
            if (rule.RestOfMorphsRS[index].Guid == fromGuid) rule.RestOfMorphsRS[index] = to;
        return
        [
            new ExpectedEffect(id, SnapshotFields.MoMorphAdhocProhibFirstMorpheme, firstBefore,
                ReferenceFieldSnapshotting.ReadAlternatives(rule.FirstMorphemeRA)),
            new ExpectedEffect(id, SnapshotFields.MoMorphAdhocProhibRestOfMorphs, restBefore,
                ReferenceSequenceFieldSnapshotting.ReadAlternatives(rule.RestOfMorphsRS)),
        ];
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (id, rule) = TargetResolution.Resolve<IMoMorphAdhocProhib>(cache, operation, Kind);
        var first = ReferenceFieldSnapshotting.ReadAlternatives(rule.FirstMorphemeRA);
        var rest = ReferenceSequenceFieldSnapshotting.ReadAlternatives(rule.RestOfMorphsRS);
        return
        [
            new ExpectedEffect(id, SnapshotFields.MoMorphAdhocProhibFirstMorpheme, first, first),
            new ExpectedEffect(id, SnapshotFields.MoMorphAdhocProhibRestOfMorphs, rest, rest),
        ];
    }
}

internal sealed class BundleMsaHandler : IOperationHandler
{
    internal static readonly BundleMsaHandler Instance = new();
    private const string Kind = MsaReferenceRetargetOperationKinds.SetBundleMsa;
    private BundleMsaHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = MsaReferenceRetargetPayload.Parse(OperationSlotRegistry.After(operation), Kind);
        var (id, bundle) = TargetResolution.Resolve<IWfiMorphBundle>(cache, operation, Kind);
        touchedTargets.Add(id);
        var to = ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, payload.To, Kind);
        if (bundle.MsaRA?.Guid != payload.From.ToGuid())
            throw new InvalidOperationException(
                $"'{Kind}': the bundle no longer names '{payload.From.Value}'; recompute the Draft.");
        var entryGuid = bundle.MsaRA.Owner?.Guid;
        if (to.Owner?.Guid != entryGuid || (bundle.MorphRA is { } morph && morph.Owner?.Guid != entryGuid))
            throw new InvalidOperationException(
                $"'{Kind}': '{payload.To.Value}' must belong to the entry of the bundle's current grammatical info and morph.");
        if (to is not (IMoInflAffMsa or IMoDerivAffMsa or IMoUnclassifiedAffixMsa))
            throw new InvalidOperationException($"'{Kind}': '{payload.To.Value}' is not the grammatical info of an affix.");
        var before = ReferenceFieldSnapshotting.ReadAlternatives(bundle.MsaRA);
        bundle.MsaRA = to;
        return new ExpectedEffect(id, SnapshotFields.WfiMorphBundleMsa, before,
            ReferenceFieldSnapshotting.ReadAlternatives(bundle.MsaRA));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, bundle) = TargetResolution.Resolve<IWfiMorphBundle>(cache, operation, Kind);
        var current = ReferenceFieldSnapshotting.ReadAlternatives(bundle.MsaRA);
        return new ExpectedEffect(id, SnapshotFields.WfiMorphBundleMsa, current, current);
    }
}
