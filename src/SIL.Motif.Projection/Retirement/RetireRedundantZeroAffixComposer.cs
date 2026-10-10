using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;

namespace SIL.Motif.Projection.Retirement;

public static class RetireRedundantZeroAffixComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache,
        RetireRedundantZeroAffixIntentDocument document, Func<CanonicalId> operationIdFactory)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operationIdFactory);
        _ = RetireRedundantZeroAffixCodec.ToJson(document);
        if (!CanonicalId.TryParse(document.Retirement.Entry, out var entryId))
            throw new InvalidOperationException("The zero-affix entry is not a canonical id.");

        var graph = RedundantZeroAffixGraphReader.Read(cache, entryId);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId.ToGuid());
        var msa = entry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Single();
        var removals = graph.OptionalSlots.Select(slot => new OperationEnvelope(operationIdFactory(),
            MoInflAffMsaSlotsOperationKinds.RemoveRefSlots,
            target: CanonicalId.FromGuid(msa.Guid),
            after: JsonSerializer.SerializeToElement(new { member = slot.Id.Value }, JsonOptions))).ToArray();
        var deleteId = operationIdFactory();
        if (removals.Select(item => item.OperationId).Append(deleteId).Distinct().Count() != removals.Length + 1)
            throw new InvalidOperationException("The operation id factory returned a duplicate id.");
        var delete = new OperationEnvelope(deleteId, RetireRedundantZeroAffixOperationKinds.DeleteGraph,
            target: entryId,
            after: JsonSerializer.SerializeToElement(new
            {
                graphDigest = graph.Digest,
                members = graph.Members.Select(item => new
                {
                    id = item.Id.Value,
                    item.ClassName,
                    owner = item.Owner?.Value,
                }),
                optionalSlots = graph.OptionalSlots.Select(item => item.Id.Value),
            }, JsonOptions),
            dependsOn: removals.Select(item => new OperationDependency(item.OperationId)).ToArray());
        return removals.Append(delete).ToArray();
    }
}
