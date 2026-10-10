using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

public static partial class RetireRedundantZeroAffixOperationKinds
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeleteGraph);
        OperationHandlerRegistry.Register(DeleteGraph, RetireRedundantZeroAffixGraphDeleteHandler.Instance);
    }
#pragma warning restore CA2255
}

public sealed record RetireRedundantZeroAffixPayload(string GraphDigest,
    IReadOnlyList<RedundantZeroAffixGraphMember> Members, IReadOnlyList<CanonicalId> OptionalSlots)
{
    public static RetireRedundantZeroAffixPayload Parse(JsonElement after)
    {
        const string kind = RetireRedundantZeroAffixOperationKinds.DeleteGraph;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["graphDigest", "members", "optionalSlots"], kind);
        var digest = ClosedPayloadParsing.GetRequiredString(after, "graphDigest", kind);
        if (!Sha256Value.IsCanonical(digest))
            throw new ContractParseException($"'{kind}' requires a canonical graph digest.");
        if (!after.TryGetProperty("members", out var membersValue) || membersValue.ValueKind != JsonValueKind.Array ||
            membersValue.GetArrayLength() == 0)
            throw new ContractParseException($"'{kind}' requires a nonempty owned-member list.");
        var members = new List<RedundantZeroAffixGraphMember>();
        foreach (var item in membersValue.EnumerateArray())
        {
            ClosedPayloadParsing.RequireObject(item, kind + ".members");
            ClosedPayloadParsing.RejectUnknownProperties(item, ["id", "className", "owner"], kind + ".members");
            var id = ClosedPayloadParsing.GetRequiredCanonicalId(item, "id", kind + ".members");
            var className = ClosedPayloadParsing.GetRequiredString(item, "className", kind + ".members");
            CanonicalId? owner = null;
            if (item.TryGetProperty("owner", out var ownerValue) && ownerValue.ValueKind != JsonValueKind.Null)
            {
                if (ownerValue.ValueKind != JsonValueKind.String || !CanonicalId.TryParse(ownerValue.GetString(), out var parsed))
                    throw new ContractParseException($"'{kind}.members.owner' must be null or a canonical id.");
                owner = parsed;
            }
            members.Add(new RedundantZeroAffixGraphMember(id, className, owner));
        }
        if (members.Select(item => item.Id).Distinct().Count() != members.Count)
            throw new ContractParseException($"'{kind}' owned-member ids must be unique.");
        if (!after.TryGetProperty("optionalSlots", out var slotsValue) || slotsValue.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' requires an optionalSlots array.");
        var slots = slotsValue.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String || !CanonicalId.TryParse(item.GetString(), out var id))
                throw new ContractParseException($"'{kind}' optional slot ids must be canonical ids.");
            return id;
        }).ToArray();
        if (slots.Distinct().Count() != slots.Length)
            throw new ContractParseException($"'{kind}' optional slot ids must be unique.");
        return new RetireRedundantZeroAffixPayload(digest, Array.AsReadOnly(members.ToArray()), Array.AsReadOnly(slots));
    }
}

internal sealed class RetireRedundantZeroAffixGraphDeleteHandler : IOperationHandler
{
    internal static readonly RetireRedundantZeroAffixGraphDeleteHandler Instance = new();
    private RetireRedundantZeroAffixGraphDeleteHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation,
        List<CanonicalId> touchedTargets)
    {
        var payload = Parse(operation);
        var id = ResolveEntryId(operation);
        var graph = RedundantZeroAffixGraphReader.Read(cache, id);
        RequireSameGraph(graph, payload);
        if (graph.OptionalSlots.Count != 0)
            throw new InvalidOperationException("Optional slot references must be removed before deleting the entry graph.");
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.TryGetObject(id.ToGuid(), out var value) || value is not ILexEntry entry)
            throw new InvalidOperationException("The zero-affix entry disappeared before its owning delete.");
        touchedTargets.Add(id);
        entry.Delete();
        RedundantZeroAffixGraphReader.VerifyDeleted(cache, graph);
        return new ExpectedEffect(id, SnapshotFields.RetireRedundantZeroAffixGraph,
            GraphValue(payload.GraphDigest), EmptyValue(), Preview(payload));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var payload = Parse(operation);
        var id = ResolveEntryId(operation);
        var graph = RedundantZeroAffixGraphReader.Read(cache, id);
        RequireSameGraph(graph, payload);
        if (!graph.OptionalSlots.Select(item => item.Id).SequenceEqual(payload.OptionalSlots))
            throw new InvalidOperationException("The zero-affix entry's optional slot references changed after composition.");
        return new ExpectedEffect(id, SnapshotFields.RetireRedundantZeroAffixGraph,
            GraphValue(graph.Digest), GraphValue(graph.Digest), Preview(payload));
    }

    private static RetireRedundantZeroAffixPayload Parse(OperationEnvelope operation) =>
        RetireRedundantZeroAffixPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{RetireRedundantZeroAffixOperationKinds.DeleteGraph}' requires 'after'."));

    private static CanonicalId ResolveEntryId(OperationEnvelope operation) => operation.Target ??
        throw new ContractParseException($"'{RetireRedundantZeroAffixOperationKinds.DeleteGraph}' requires 'target'.");

    private static void RequireSameGraph(RedundantZeroAffixGraph graph, RetireRedundantZeroAffixPayload payload)
    {
        if (!StringComparer.Ordinal.Equals(graph.Digest, payload.GraphDigest) ||
            !graph.Members.SequenceEqual(payload.Members))
            throw new InvalidOperationException("The zero-affix entry graph changed after retirement composition.");
    }

    private static IReadOnlyDictionary<string, string> GraphValue(string digest) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["graph"] = digest };

    private static IReadOnlyDictionary<string, string> EmptyValue() => new Dictionary<string, string>(StringComparer.Ordinal);

    private static JsonElement Preview(RetireRedundantZeroAffixPayload payload) => JsonSerializer.SerializeToElement(new
    {
        graphDigest = payload.GraphDigest,
        members = payload.Members.Select(item => new
        {
            id = item.Id.Value,
            item.ClassName,
            owner = item.Owner?.Value,
        }),
        optionalSlots = payload.OptionalSlots.Select(item => item.Value),
    });
}
