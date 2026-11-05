using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

internal static class PhonologicalRuleOutcomeGraph
{
    internal const string Field = "grammar/phSegRuleRHS/deletedGraph";

    internal static IReadOnlyList<ICmObject> Members(IPhSegRuleRHS rhs)
    {
        var members = OwnedGraphGuard.Members(rhs).ToDictionary(o => o.Guid);
        foreach (var context in members.Values.OfType<IPhPhonContext>().ToArray()) Visit(context);
        foreach (var feature in rhs.ReqRuleFeatsRC.Concat(rhs.ExclRuleFeatsRC)) members.TryAdd(feature.Guid, feature);
        return members.Values.OrderBy(o => o.Guid).ToArray();

        void Visit(IPhPhonContext context)
        {
            members.TryAdd(context.Guid, context);
            if (context is IPhSequenceContext sequence)
                foreach (var child in sequence.MembersRS)
                    if (!members.ContainsKey(child.Guid)) Visit(child);
            if (context is IPhIterationContext { MemberRA: { } member } && !members.ContainsKey(member.Guid)) Visit(member);
            if (context is IPhSimpleContextNC nc)
                foreach (var constraint in nc.PlusConstrRS.Concat(nc.MinusConstrRS))
                    members.TryAdd(constraint.Guid, constraint);
        }
    }

    internal static string Digest(LcmCache cache, IPhSegRuleRHS rhs)
    {
        var graph = JsonSerializer.Serialize(Members(rhs).Select(o => new { o.Guid, digest = OwnedGraphGuard.Digest(cache, o) }));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graph))).ToLowerInvariant();
    }
}

internal sealed class PhonologicalRuleOutcomeDeleteHandler : ICompoundOperationHandler
{
    private const string Kind = PhonologicalRuleDeleteOperationKinds.DeleteRightHandSides;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(payload, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(payload, ["graphDigest"], Kind);
        var expected = ClosedPayloadParsing.GetRequiredString(payload, "graphDigest", Kind);
        var (id, rhs) = Resolve(cache, operation);
        var graph = Graph(cache, rhs);
        if (graph["digest"] != expected)
            throw new InvalidOperationException("The right-hand-side contents changed; reread the rule and recompose its replacement.");
        var identity = ReferenceFieldAlternatives.ToAlternatives(id);
        touchedTargets.AddRange(PhonologicalRuleOutcomeGraph.Members(rhs).Select(o => CanonicalId.FromGuid(o.Guid)));
        rhs.Delete();
        return [new(id, SnapshotFields.PhRegularRuleRightHandSides, identity, ReferenceFieldAlternatives.ToAlternatives(null)),
            new(id, PhonologicalRuleOutcomeGraph.Field, graph, new Dictionary<string, string>())];
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) => ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (id, rhs) = Resolve(cache, operation);
        var identity = ReferenceFieldAlternatives.ToAlternatives(id);
        var graph = Graph(cache, rhs);
        return [new(id, SnapshotFields.PhRegularRuleRightHandSides, identity, identity),
            new(id, PhonologicalRuleOutcomeGraph.Field, graph, graph)];
    }

    private static Dictionary<string, string> Graph(LcmCache cache, IPhSegRuleRHS rhs) =>
        new() { ["digest"] = PhonologicalRuleOutcomeGraph.Digest(cache, rhs) };

    private static (CanonicalId, IPhSegRuleRHS) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var (id, rhs) = TargetResolution.Resolve<IPhSegRuleRHS>(cache, operation, Kind);
        if (rhs.Owner is not IPhRegularRule)
            throw new InvalidOperationException($"'{Kind}': '{id.Value}' is not a regular rule's right-hand side.");
        return (id, rhs);
    }
}

internal sealed class PhonologicalRuleConstraintCollectionHandler : IOperationHandler
{
    internal const string Kind = "grammar/phRegularRule/collectUnusedFeatConstraints";
    private const string Field = "grammar/phRegularRule/retiredConstraints";

    internal static CanonicalId[] Constraints(OperationEnvelope operation)
    {
        var payload = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(payload, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(payload, ["constraints"], Kind);
        return RetargetPayloadParsing.Ids(payload, "constraints", Kind).ToArray();
    }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, _) = TargetResolution.Resolve<IPhRegularRule>(cache, operation, Kind);
        var before = Read(cache, operation);
        foreach (var constraintId in Constraints(operation))
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(constraintId.ToGuid(), out var value)) continue;
            if (value is not IPhFeatureConstraint constraint || constraint.Owner is not IPhPhonData)
                throw new InvalidOperationException("Only pooled phonological alpha constraints can be collected.");
            if (constraint.ReferringObjects.Any()) continue;
            touchedTargets.Add(constraintId);
            constraint.Delete();
        }
        return new(id, Field, before, Read(cache, operation));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, _) = TargetResolution.Resolve<IPhRegularRule>(cache, operation, Kind);
        var current = Read(cache, operation);
        return new(id, Field, current, current);
    }

    private static Dictionary<string, string> Read(LcmCache cache, OperationEnvelope operation) => Constraints(operation).ToDictionary(
        id => id.Value, id => cache.ServiceLocator.ObjectRepository.TryGetObject(id.ToGuid(), out var value)
            ? string.Join(",", value.ReferringObjects.Select(o => o.Guid).Order()) : "absent");
}
