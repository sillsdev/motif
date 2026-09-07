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
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the two <c>FsFeatStruc.FeatureSpecs</c> operation kinds -- the first hand-written grammar
/// owning/<b>col</b> slot, alongside the owning/atomic <c>MoStemMsa.MsFeatures</c> and
/// <c>LexEntry.LexemeForm</c> precedents (ADR 0022 §4, "creation validity"). Two decisions this
/// family had to make that those precedents did not:
/// <list type="bullet">
/// <item><description><b>Concrete subclass:</b> <c>FsFeatureSpecification</c> is <c>abstract="true"</c>,
/// so <c>create</c> must name a concrete subclass (ADR 0023 decision 2). <c>FsClosedValue</c> is the
/// one built here -- the ordinary case, an enumerated choice against a closed feature (e.g.
/// Number=Singular). <c>FsOpenValue</c>, <c>FsComplexValue</c>, <c>FsDisjunctiveValue</c>,
/// <c>FsNegatedValue</c>, and <c>FsSharedValue</c> are out of scope: nothing in Motif authors them
/// yet, and adding a dispatch table with no caller would be speculative.</description></item>
/// <item><description><b>What a newly created spec must carry:</b> nothing. Checked directly against
/// <c>sillsdev/liblcm</c>'s <c>OverridesCellar.cs</c> (the hand-written half of <c>FsClosedValue</c>,
/// <c>FsFeatureSpecification</c>, and <c>FsFeatStruc</c>): <c>FsClosedValue.GetFeatureValueString</c>
/// explicitly handles <c>FeatureRA == null</c> (renders a placeholder), <c>ValueRA == null</c> is a
/// named state ("filled in by FillInBlanks"), and neither the factory nor
/// <c>AddObjectSideEffectsInternal</c> reject a bare, both-null <c>FsClosedValue</c>. Same shape as
/// <c>MoStemMsaMsFeatures</c>: LibLCM imposes no minimum on a newly created member, so <c>create</c>
/// carries a closed, empty payload here too. Populating <c>Feature</c> is a separate operation against
/// the new spec's own identity, reusing the already-generated
/// <see cref="FsFeatureSpecificationFeatureOperationKinds"/> rather than folding it into this
/// <c>create</c>. Populating the chosen <c>FsClosedValue.Value</c> needs that field's own generated
/// <c>set</c>/<c>clear</c> kind, which does not exist yet -- a later generator slice, not this
/// hand-written one.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <b>owning/col, not owning/atomic:</b> a collection member has its own identity from the moment it
/// is proposed, so unlike <c>MoStemMsaMsFeatures</c> (whose delete targets the MSA, because an atomic
/// slot's sole occupant needs no separate discriminator), <c>create</c> targets the owning
/// <c>FsFeatStruc</c> (there is no other way to name a member that does not exist yet) while
/// <c>delete</c> targets the member itself directly -- the same shape
/// <see cref="FsFeatureSpecificationFeatureOperationKinds"/> already uses for a spec's fields.
/// </remarks>
public static class FsFeatStrucFeatureSpecsOperationKinds
{
    public const string CreateFeatureSpecs = "grammar/fsFeatStruc/createFeatureSpecs";
    public const string DeleteFeatureSpecs = "grammar/fsFeatStruc/deleteFeatureSpecs";

    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(CreateFeatureSpecs);
        OperationKindRegistry.Register(DeleteFeatureSpecs);
        OperationHandlerRegistry.Register(CreateFeatureSpecs, FsFeatStrucFeatureSpecsCreateHandler.Instance);
        OperationHandlerRegistry.Register(DeleteFeatureSpecs, FsFeatStrucFeatureSpecsDeleteHandler.Instance);
    }
}

/// <summary>
/// The <c>after</c> payload for both kinds: <c>{}</c>. Closed: any property is rejected -- a fresh
/// <c>FsClosedValue</c> starts with neither feature nor value, and a delete needs nothing beyond
/// which member its <c>target</c> already names.
/// </summary>
public static class FsFeatStrucFeatureSpecsPayload
{
    private static readonly string[] AllowedProperties = Array.Empty<string>();

    public static void Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, AllowedProperties, kind);
    }
}

/// <summary>
/// Lowers <see cref="FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs"/>: creates a bare
/// <c>FsClosedValue</c> with the authored identity and adds it to <c>featStruc.FeatureSpecsOC</c>.
/// </summary>
/// <remarks>Must run inside an already-open unit of work (ADR 0006 decision 5).</remarks>
public static class FsFeatStrucFeatureSpecsCreateLowering
{
    public static IFsFeatureSpecification Apply(LcmCache cache, IFsFeatStruc featStruc, Guid newSpecId)
    {
        var newSpec = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create(newSpecId);
        featStruc.FeatureSpecsOC.Add(newSpec);
        return newSpec;
    }
}

/// <summary>
/// Lowers <see cref="FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs"/> through LibLCM's
/// ownership cascade.
/// </summary>
public static class FsFeatStrucFeatureSpecsDeleteLowering
{
    public static void Apply(IFsFeatureSpecification spec) => spec.Delete();
}

/// <summary>Resolves, snapshots, lowers, and re-snapshots one
/// <see cref="FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs"/> operation.</summary>
internal sealed class FsFeatStrucFeatureSpecsCreateHandler : IOperationHandler
{
    internal static readonly FsFeatStrucFeatureSpecsCreateHandler Instance = new();
    private FsFeatStrucFeatureSpecsCreateHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var entityId = RequireEntityId(operation);

        if (operation.After is not { } after)
        {
            throw new InvalidOperationException(
                $"Operation '{operation.OperationId.Value}' of kind '{FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs}' requires 'after'.");
        }

        FsFeatStrucFeatureSpecsPayload.Parse(after, FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs);

        var (id, featStruc) = TargetResolution.Resolve<IFsFeatStruc>(cache, operation, FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs);
        touchedTargets.Add(id);

        var before = MembershipSnapshotting.ReadAlternatives(featStruc, entityId.ToGuid());
        FsFeatStrucFeatureSpecsCreateLowering.Apply(cache, featStruc, entityId.ToGuid());
        var afterValue = ReferenceFieldAlternatives.ToAlternatives(entityId);

        return new ExpectedEffect(id, SnapshotFields.FsFeatStrucFeatureSpecs, before, afterValue);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = RequireEntityId(operation);
        var (id, featStruc) = TargetResolution.Resolve<IFsFeatStruc>(cache, operation, FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs);
        var current = MembershipSnapshotting.ReadAlternatives(featStruc, entityId.ToGuid());
        return new ExpectedEffect(id, SnapshotFields.FsFeatStrucFeatureSpecs, current, current);
    }

    private static CanonicalId RequireEntityId(OperationEnvelope operation) =>
        operation.EntityId ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs}' requires 'entityId'.");
}

/// <summary>The <see cref="FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs"/> counterpart to
/// <see cref="FsFeatStrucFeatureSpecsCreateHandler"/>. Targets the member's own identity directly,
/// the same shape <see cref="FsFeatureSpecificationFeatureOperationKinds"/> uses.</summary>
internal sealed class FsFeatStrucFeatureSpecsDeleteHandler : IOperationHandler
{
    internal static readonly FsFeatStrucFeatureSpecsDeleteHandler Instance = new();
    private FsFeatStrucFeatureSpecsDeleteHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.After is not { } after)
        {
            throw new InvalidOperationException(
                $"Operation '{operation.OperationId.Value}' of kind '{FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs}' requires 'after'.");
        }

        FsFeatStrucFeatureSpecsPayload.Parse(after, FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs);

        var (id, spec) = ResolveMember(cache, operation);
        touchedTargets.Add(id);

        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        FsFeatStrucFeatureSpecsDeleteLowering.Apply(spec);
        var afterValue = ReferenceFieldAlternatives.ToAlternatives(null);

        return new ExpectedEffect(id, SnapshotFields.FsFeatStrucFeatureSpecs, before, afterValue);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, _) = ResolveMember(cache, operation);
        var current = ReferenceFieldAlternatives.ToAlternatives(id);
        return new ExpectedEffect(id, SnapshotFields.FsFeatStrucFeatureSpecs, current, current);
    }

    /// <summary>Resolves target, requiring it be owned through FeatureSpecs, not FsFeatDefn.Default.</summary>
    private static (CanonicalId Id, IFsFeatureSpecification Spec) ResolveMember(LcmCache cache, OperationEnvelope operation)
    {
        var (id, spec) = TargetResolution.Resolve<IFsFeatureSpecification>(cache, operation, FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs);
        if (spec.Owner is not IFsFeatStruc)
        {
            throw new InvalidOperationException(
                $"'{FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs}' operation: '{id.Value}' is not " +
                "a member of an FsFeatStruc's FeatureSpecs collection.");
        }

        return (id, spec);
    }
}

/// <summary>
/// Reads whether one specific, already-minted <see cref="CanonicalId"/> is currently a member of
/// <paramref name="featStruc"/>'s <c>FeatureSpecsOC</c> -- the owning/col analogue of
/// <see cref="SIL.Motif.Runner.Snapshotting.ReferenceFieldSnapshotting"/>, at the granularity of one
/// member's presence rather than one slot's occupant.
/// </summary>
internal static class MembershipSnapshotting
{
    public static IReadOnlyDictionary<string, string> ReadAlternatives(IFsFeatStruc featStruc, Guid candidateId)
    {
        var isMember = featStruc.FeatureSpecsOC.Any(member => member.Guid == candidateId);
        return ReferenceFieldAlternatives.ToAlternatives(isMember ? CanonicalId.FromGuid(candidateId) : (CanonicalId?)null);
    }
}
