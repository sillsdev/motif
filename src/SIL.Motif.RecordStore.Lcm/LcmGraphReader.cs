using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Application;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.RecordStore.Lcm;

public sealed class LcmGraphReader
{
    private readonly LcmCache cache;
    private readonly IFwMetaDataCacheManaged metadata;
    private readonly Dictionary<int, ICmObject> objects;
    private readonly Dictionary<int, int[]> layouts;
    private readonly FrozenDictionary<int, int> fieldTypes;
    private readonly FrozenDictionary<int, string> fieldKeys;
    private readonly Dictionary<int, NativeProvenance> provenance = new();
    private static readonly JsonElement NullValue = JsonSerializer.SerializeToElement<object?>(null);
    private static readonly JsonElement TrueValue = JsonSerializer.SerializeToElement(true);
    private static readonly JsonElement FalseValue = JsonSerializer.SerializeToElement(false);
    private static readonly JsonElement ZeroValue = JsonSerializer.SerializeToElement(0);
    private static readonly JsonElement EmptyVector = JsonSerializer.SerializeToElement(Array.Empty<Guid>());
    private static readonly JsonElement EmptyAlternatives = JsonSerializer.SerializeToElement(new Dictionary<string, object>());
    public static string TraversalVersion { get; } = "LCModel/" + typeof(LcmCache).Assembly.GetName().Version + "/" +
        typeof(LcmCache).Assembly.ManifestModule.ModuleVersionId.ToString("D") + "/repository-and-vectors/1";
    private readonly Dictionary<int, (int Owner, int Field, int Ordinal)> ownership = new();

    public LcmGraphReader(LcmCache cache)
    {
        this.cache = cache;
        metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        fieldTypes = metadata.GetFieldIds().Where(f => !metadata.get_IsVirtual(f)).ToFrozenDictionary(f => f, metadata.GetFieldType);
        fieldKeys = fieldTypes.Keys.ToFrozenDictionary(field => field, field => field.ToString(CultureInfo.InvariantCulture));
        layouts = metadata.GetClassIds().ToDictionary(c => c, c => metadata.GetFields(c, true, -1)
            .Where(f => !metadata.get_IsVirtual(f)).Order().ToArray());
        var encountered = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().ToArray();
        objects = encountered.ToDictionary(o => o.Hvo);
        var repositories = new Dictionary<int, Dictionary<string, long>>();
        var traversals = new Dictionary<string, IEnumerable<ICmObject>>(StringComparer.Ordinal)
        {
            ["MoForm"] = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances(),
            ["WfiMorphBundle"] = cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().AllInstances(),
            ["WfiWordform"] = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances(),
            ["WfiAnalysis"] = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances(),
            ["WfiGloss"] = cache.ServiceLocator.GetInstance<IWfiGlossRepository>().AllInstances(),
            ["LexEntry"] = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances(),
            ["LexSense"] = cache.ServiceLocator.GetInstance<ILexSenseRepository>().AllInstances(),
            ["MoMorphSynAnalysis"] = cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>().AllInstances(),
            ["LexEntryRef"] = cache.ServiceLocator.GetInstance<ILexEntryRefRepository>().AllInstances(),
            ["CmAgentEvaluation"] = cache.ServiceLocator.GetInstance<ICmAgentEvaluationRepository>().AllInstances()
        };
        foreach (var (name, traversal) in traversals)
        {
            long position = 0;
            foreach (var value in traversal)
            {
                if (!objects.ContainsKey(value.Hvo)) throw new InvalidDataException("Typed repository object is absent from the full graph.");
                if (!repositories.TryGetValue(value.Hvo, out var positions)) repositories.Add(value.Hvo, positions = new(StringComparer.Ordinal));
                positions.Add(name, position++);
            }
        }
        var data = (ISilDataAccessManaged)cache.DomainDataByFlid;
        for (var ordinal = 0; ordinal < encountered.Length; ordinal++)
        {
            var value = encountered[ordinal];
            var vectors = ImmutableDictionary.CreateBuilder<int, ImmutableArray<Guid>>();
            foreach (var field in Fields(value.ClassID))
            {
                var type = fieldTypes[field];
                if (type == 23)
                {
                    var target = data.get_ObjectProp(value.Hvo, field);
                    if (target != 0) ownership.Add(target, (value.Hvo, field, 0));
                }
                if (type < 25) continue;
                var targets = data.VecProp(value.Hvo, field);
                if (targets.Length == 0) continue;
                var items = ImmutableArray.CreateBuilder<Guid>(targets.Length);
                for (var index = 0; index < targets.Length; index++)
                {
                    var target = targets[index];
                    items.Add(objects[target].Guid);
                    if (type is 25 or 27) ownership.Add(target, (value.Hvo, field, type == 27 ? index : 0));
                }
                vectors.Add(field, items.MoveToImmutable());
            }
            provenance.Add(value.Hvo, new NativeProvenance(ordinal, TraversalVersion, vectors.ToImmutable())
            {
                RepositoryOrdinals = repositories.TryGetValue(value.Hvo, out var positions)
                    ? positions.ToImmutableDictionary(StringComparer.Ordinal) : ImmutableDictionary<string, long>.Empty
            });
        }
        foreach (var value in objects.Values)
            if ((value.Owner?.Hvo ?? 0) != ownership.GetValueOrDefault(value.Hvo).Owner)
                throw new InvalidDataException($"Inconsistent ownership for {value.Guid}.");
    }

    private int[] Fields(int classId) => layouts[classId];
    /// <summary>Throws when the project's built-in metadata is not the metadata behind the static layout table.</summary>
    public void RequirePinnedMetadata() => LcmLayoutTable.Require(metadata);

    public IReadOnlyDictionary<int, int> FieldTypes => fieldTypes;

    public NativeImportCoverage Coverage() => NativeImportCoverage.Capture(objects.Values.Select(o =>
        new ReferenceRecord(o.Guid, o.ClassID, o.Owner?.Guid, ownership.GetValueOrDefault(o.Hvo).Field)),
        layouts.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableDictionary(f => f, metadata.GetFieldType)), TraversalVersion, Schema());

    public JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        classes = metadata.GetClassIds().Order().Select(c => new
        {
            id = c, name = metadata.GetClassName(c), isAbstract = metadata.GetAbstract(c), baseId = c == 0 ? (int?)null : metadata.GetBaseClsId(c)
        }).ToArray(),
        fields = metadata.GetFieldIds().Where(f => !metadata.get_IsVirtual(f)).Order().Select(f => new
        {
            id = f, name = metadata.GetFieldName(f), owner = metadata.GetOwnClsName(f), type = metadata.GetFieldType(f),
            destination = metadata.GetDstClsId(f), custom = metadata.IsCustom(f), ws = metadata.GetFieldWs(f),
            label = metadata.GetFieldLabel(f), help = metadata.GetFieldHelp(f), xml = metadata.GetFieldXml(f),
            listRoot = metadata.IsCustom(f) ? metadata.GetFieldListRoot(f).ToString("D") : null,
            identity = metadata.IsCustom(f)
                ? LcmLayoutTable.CustomIdentity(metadata.GetOwnClsName(f), metadata.GetFieldName(f), metadata.GetFieldType(f), metadata.GetDstClsId(f)) : null
        }).ToArray()
    });

    // LCModel is read on this thread; a chunk's values then become JSON in parallel, from plain objects only.
    public IReadOnlyList<GenericRecord> Read() => ReadRecords().ToArray();

    // Streams one chunk at a time so a very large project need not hold every record at once.
    internal IEnumerable<GenericRecord> ReadRecords()
    {
        const int Chunk = 1024;
        var ordered = objects.Values.OrderBy(o => o.Guid).ToArray();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
        for (var start = 0; start < ordered.Length; start += Chunk)
        {
            var count = Math.Min(Chunk, ordered.Length - start);
            var records = new GenericRecord[count];
            var extracted = new object?[count][];
            var heads = new (Guid Id, int ClassId, Guid? Owner, int Hvo)[count];
            for (var index = 0; index < count; index++)
            {
                var value = ordered[start + index];
                heads[index] = (value.Guid, value.ClassID, value.Owner?.Guid, value.Hvo);
                extracted[index] = Fields(value.ClassID).Select(field => Value(value, field)).ToArray();
            }
            try
            {
                Parallel.For(0, count, options, index =>
                {
                    var head = heads[index];
                    var fields = Fields(head.ClassId);
                    var values = new Dictionary<string, JsonElement>(fields.Length);
                    for (var field = 0; field < fields.Length; field++) values.Add(fieldKeys[fields[field]], Json(extracted[index][field]));
                    records[index] = new GenericRecord
                    {
                        Provenance = provenance[head.Hvo],
                        Id = head.Id, ClassId = head.ClassId, Owner = head.Owner,
                        OwningField = ownership.GetValueOrDefault(head.Hvo).Field,
                        Values = values
                    };
                });
            }
            catch (AggregateException failure) when (failure.InnerExceptions.Count > 0)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerExceptions[0]).Throw();
            }
            foreach (var record in records) yield return record;
        }
    }

    private static JsonElement Json(object? extracted) => extracted switch
    {
        null => NullValue,
        true => TrueValue,
        false => FalseValue,
        0 => ZeroValue,
        ImmutableArray<Guid> { IsEmpty: true } => EmptyVector,
        SortedDictionary<string, object?> { Count: 0 } => EmptyAlternatives,
        var value => JsonSerializer.SerializeToElement(value)
    };

    public static string Canonical(GenericRecord record) => CanonicalJson.Serialize(new
    {
        guid = record.Id.ToString("D"), classId = record.ClassId, owner = record.Owner?.ToString("D"),
        owningField = record.OwningField, fields = record.Values
    });

    private Guid? Link(int hvo) => hvo == 0 ? null : objects[hvo].Guid;

    private object? Value(ICmObject value, int field)
    {
        var data = (ISilDataAccessManaged)cache.DomainDataByFlid;
        var hvo = value.Hvo;
        var type = fieldTypes[field];
        switch ((CellarPropertyType)type)
        {
            case CellarPropertyType.Boolean: return data.get_BooleanProp(hvo, field);
            case CellarPropertyType.Integer:
                if (field == 104) return ownership.GetValueOrDefault(hvo).Field;
                if (field == 105 && ownership.TryGetValue(hvo, out var edge) && fieldTypes[edge.Field] == 27) return edge.Ordinal;
                return data.get_IntProp(hvo, field);
            case CellarPropertyType.Guid: return data.get_GuidProp(hvo, field);
            case CellarPropertyType.Time:
                var time = data.get_DateTime(hvo, field);
                return new { ticks = time.Ticks.ToString(CultureInfo.InvariantCulture), kind = time.Kind.ToString() };
            case CellarPropertyType.GenDate:
                var date = data.get_GenDateProp(hvo, field);
                return new { year = date.Year, month = date.Month, day = date.Day, precision = date.Precision.ToString(), isAD = date.IsAD };
            case CellarPropertyType.Unicode: return LcmText.Raw(data.get_UnicodeProp(hvo, field));
            case CellarPropertyType.String: return LcmText.Text(cache, data.get_StringProp(hvo, field));
            case CellarPropertyType.MultiString:
            case CellarPropertyType.MultiUnicode:
                var multi = data.get_MultiStringProp(hvo, field);
                var alternatives = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                for (var index = 0; index < multi.StringCount; index++)
                {
                    var text = multi.GetStringFromIndex(index, out var handle);
                    alternatives.Add(cache.ServiceLocator.WritingSystemManager.Get(handle).Id, LcmText.Text(cache, text));
                }
                return alternatives;
            case CellarPropertyType.Binary:
                if (value.GetType().GetProperty(metadata.GetFieldName(field))?.PropertyType == typeof(ITsTextProps))
                    return new { textProps = LcmText.Properties(cache, (ITsTextProps?)data.get_UnknownProp(hvo, field)) };
                data.get_Binary(hvo, field, out byte[] bytes);
                return new { base64 = bytes is null ? null : Convert.ToBase64String(bytes) };
            case CellarPropertyType.OwningAtomic:
            case CellarPropertyType.ReferenceAtomic: return Link(data.get_ObjectProp(hvo, field));
            case CellarPropertyType.OwningCollection:
            case CellarPropertyType.ReferenceCollection:
            case CellarPropertyType.OwningSequence:
            case CellarPropertyType.ReferenceSequence:
                var links = data.VecProp(hvo, field).Select(target => objects[target].Guid).ToImmutableArray();
                return type is 25 or 26 ? links.Sort() : links;
            default: throw new NotSupportedException($"Unsupported field type {type} for {value.Guid}/{field}.");
        }
    }
}
