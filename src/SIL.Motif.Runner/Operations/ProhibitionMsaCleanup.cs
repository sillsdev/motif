using System.Globalization;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;

namespace SIL.Motif.Runner.Operations;

internal sealed class ProhibitionMsaCleanup
{
    internal const string AnalysisField = "grammar/moMorphSynAnalysis/ownedGraph";
    private readonly List<(CanonicalId Id, Dictionary<string, string> Value)> _analyses = [];
    private readonly List<(ICmObject Source, int Flid, string Field, Dictionary<string, string> Value)> _references = [];
    private readonly List<ExpectedEffect> _contexts = [];

    internal static ProhibitionMsaCleanup Read(LcmCache cache, IMoAdhocProhib rule)
    {
        var result = new ProhibitionMsaCleanup();
        if (rule is not IMoMorphAdhocProhib morph) return result;
        var candidates = new Dictionary<Guid, IMoMorphSynAnalysis>();
        void Add(IMoMorphSynAnalysis msa)
        {
            if (!candidates.TryAdd(msa.Guid, msa)) return;
            foreach (var component in msa.ComponentsRS) Add(component);
        }
        if (morph.FirstMorphemeRA is { } first) Add(first);
        foreach (var msa in morph.RestOfMorphsRS.Concat(morph.MorphemesRS)) Add(msa);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var rows = new List<OwnedGraphGuard.ReferenceRow>();
        foreach (var msa in candidates.Values.OrderBy(item => item.Guid))
        {
            var references = OwnedGraphGuard.References(cache, msa);
            rows.AddRange(references);
            result._analyses.Add((Id(msa), new()
            {
                ["entry"] = Id(msa.Owner).Value,
                ["class"] = msa.ClassName,
                ["name"] = msa.LongName,
                ["content"] = OwnedGraphGuard.Digest(cache, msa),
                ["members"] = JsonSerializer.Serialize(OwnedGraphGuard.Members(msa).Select(item => Id(item).Value)),
                ["references"] = JsonSerializer.Serialize(references),
            }));
        }
        foreach (var row in rows.DistinctBy(row => (row.Source, row.Field)).OrderBy(row => row.Source, StringComparer.Ordinal)
                     .ThenBy(row => row.Field, StringComparer.Ordinal))
        {
            var source = cache.ServiceLocator.ObjectRepository.GetObject(CanonicalId.Parse(row.Source).ToGuid());
            if (source.Guid == rule.Guid || candidates.ContainsKey(source.Guid)) continue;
            var flid = metadata.GetFieldId2(source.ClassID, row.Field, true);
            var domain = source is IWfiMorphBundle ? "analysis" : "grammar";
            var field = $"{domain}/{char.ToLowerInvariant(source.ClassName[0])}{source.ClassName[1..]}/{char.ToLowerInvariant(row.Field[0])}{row.Field[1..]}";
            result._references.Add((source, flid, field, ReadReference(cache, source, flid)));
            if (source is IWfiMorphBundle bundle)
            {
                var context = new Dictionary<string, string>
                {
                    ["analysis"] = Id(bundle.Owner).Value,
                    ["wordform"] = Id(bundle.Owner.Owner).Value,
                };
                result._contexts.Add(new(Id(source), "analysis/wfiMorphBundle/cleanupContext", context, context));
            }
        }
        return result;
    }

    internal IReadOnlyList<ExpectedEffect> Footprint() => _analyses.Select(item =>
            new ExpectedEffect(item.Id, AnalysisField, item.Value, item.Value))
        .Concat(_references.Select(item => new ExpectedEffect(Id(item.Source), item.Field, item.Value, item.Value)))
        .Concat(_contexts).ToArray();

    internal IReadOnlyList<ExpectedEffect> Changes(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touched)
    {
        var effects = new List<ExpectedEffect>();
        foreach (var (id, value) in _analyses.Where(item => !cache.ServiceLocator.ObjectRepository.IsValidObjectId(item.Id.ToGuid())))
        {
            touched.Add(id);
            effects.Add(new(id, AnalysisField, value, new Dictionary<string, string>(), Preview(
                $"Delete grammatical analysis {id.Value} ({value["name"]}) of entry {value["entry"]} and its owned content.")));
        }
        foreach (var (source, flid, field, before) in _references)
        {
            var after = ReadReference(cache, source, flid);
            if (before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value)) continue;
            var id = Id(source);
            touched.Add(id);
            var description = source is IWfiMorphBundle bundle
                ? $"word-analysis reference {id.Value}.Msa in analysis {Id(bundle.Owner).Value}"
                : $"reference {id.Value}.{field}";
            effects.Add(new(id, field, before, after, Preview($"Delete {description} to {string.Join(", ", before.Values.Except(after.Values))}.")));
        }
        return effects;

        JsonElement Preview(string summary) => JsonSerializer.SerializeToElement(new
        {
            summary, nativeProhibitionCleanup = true, operationId = operation.OperationId.Value,
        });
    }

    internal static void ValidateSlots(Proposal proposal, IEnumerable<ExpectedEffect> effects)
    {
        foreach (var effect in effects)
        {
            if (effect.Preview is not { ValueKind: JsonValueKind.Object } preview ||
                !preview.TryGetProperty("nativeProhibitionCleanup", out var cleanup) || cleanup.ValueKind != JsonValueKind.True) continue;
            var origin = preview.GetProperty("operationId").GetString();
            var deleted = effect.Field == AnalysisField;
            var targets = deleted
                ? JsonSerializer.Deserialize<string[]>(effect.Before["members"])!.Select(CanonicalId.Parse).ToHashSet()
                : [effect.CanonicalId];
            foreach (var operation in proposal.Operations.Where(operation => operation.OperationId.Value != origin))
            foreach (var slot in OperationSlotRegistry.Resolve(operation))
                if (targets.Contains(slot.Target) && (deleted || slot.Field is null || slot.Field == effect.Field))
                    throw new ContractParseException($"Native prohibition cleanup by operation {origin} and operation {operation.OperationId.Value} " +
                        $"address the same target or field slot {slot.Target.Value}.{slot.Field}; recompose the Proposal.");
        }
    }

    private static Dictionary<string, string> ReadReference(LcmCache cache, ICmObject source, int flid)
    {
        var result = new Dictionary<string, string>();
        if (!source.IsValidObject) return result;
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var data = cache.DomainDataByFlid;
        var type = (CellarPropertyType)metadata.GetFieldType(flid);
        if (type == CellarPropertyType.ReferenceAtomic)
        {
            var target = data.get_ObjectProp(source.Hvo, flid);
            if (target != 0) result["ref"] = Id(cache.ServiceLocator.ObjectRepository.GetObject(target)).Value;
        }
        else
        {
            var values = Enumerable.Range(0, data.get_VecSize(source.Hvo, flid)).Select(index =>
                Id(cache.ServiceLocator.ObjectRepository.GetObject(data.get_VecItem(source.Hvo, flid, index))).Value);
            if (type == CellarPropertyType.ReferenceCollection) values = values.Order(StringComparer.Ordinal);
            foreach (var (value, index) in values.Select((value, index) => (value, index)))
                result[index.ToString(CultureInfo.InvariantCulture)] = value;
        }
        return result;
    }

    private static CanonicalId Id(ICmObject item) => CanonicalId.FromGuid(item.Guid);
}
