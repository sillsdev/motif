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
    private readonly Dictionary<int, (int Owner, int Field, int Ordinal)> ownership = new();

    public LcmGraphReader(LcmCache cache)
    {
        this.cache = cache;
        metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        layouts = metadata.GetClassIds().ToDictionary(c => c, c => metadata.GetFields(c, true, -1)
            .Where(f => !metadata.get_IsVirtual(f)).Order().ToArray());
        objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().ToDictionary(o => o.Hvo);
        foreach (var parent in objects.Values)
        foreach (var field in Fields(parent.ClassID).Where(f => metadata.GetFieldType(f) is 23 or 25 or 27))
        {
            var type = metadata.GetFieldType(field);
            var targets = type == 23 ? new[] { cache.DomainDataByFlid.get_ObjectProp(parent.Hvo, field) }
                : ((ISilDataAccessManaged)cache.DomainDataByFlid).VecProp(parent.Hvo, field);
            for (var index = 0; index < targets.Length; index++)
                if (targets[index] != 0) ownership.Add(targets[index], (parent.Hvo, field, type == 27 ? index : 0));
        }
        foreach (var value in objects.Values)
            if ((value.Owner?.Hvo ?? 0) != ownership.GetValueOrDefault(value.Hvo).Owner)
                throw new InvalidDataException($"Inconsistent ownership for {value.Guid}.");
    }

    private int[] Fields(int classId) => layouts[classId];
    public IReadOnlyDictionary<int, int> FieldTypes => metadata.GetFieldIds().Where(f => !metadata.get_IsVirtual(f))
        .ToDictionary(f => f, metadata.GetFieldType);

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
            listRoot = metadata.IsCustom(f) ? metadata.GetFieldListRoot(f).ToString("D") : null
        }).ToArray()
    });

    public IReadOnlyList<GenericRecord> Read() => objects.Values.OrderBy(o => o.Guid.ToString("D"), StringComparer.Ordinal)
        .Select(o => new GenericRecord
        {
            Id = o.Guid, ClassId = o.ClassID, Owner = o.Owner?.Guid,
            OwningField = ownership.GetValueOrDefault(o.Hvo).Field,
            Values = Fields(o.ClassID).ToDictionary(f => f.ToString(CultureInfo.InvariantCulture), f => JsonSerializer.SerializeToElement(Value(o, f)))
        }).ToArray();

    public static string Canonical(GenericRecord record) => CanonicalJson.Serialize(new
    {
        guid = record.Id.ToString("D"), classId = record.ClassId, owner = record.Owner?.ToString("D"),
        owningField = record.OwningField, fields = record.Values
    });

    private string? Link(int hvo) => hvo == 0 ? null : objects[hvo].Guid.ToString("D");

    private object? Value(ICmObject value, int field)
    {
        var data = (ISilDataAccessManaged)cache.DomainDataByFlid;
        var hvo = value.Hvo;
        switch ((CellarPropertyType)metadata.GetFieldType(field))
        {
            case CellarPropertyType.Boolean: return data.get_BooleanProp(hvo, field);
            case CellarPropertyType.Integer:
                if (field == 104) return ownership.GetValueOrDefault(hvo).Field;
                if (field == 105 && ownership.TryGetValue(hvo, out var edge) && metadata.GetFieldType(edge.Field) == 27) return edge.Ordinal;
                return data.get_IntProp(hvo, field);
            case CellarPropertyType.Guid: return data.get_GuidProp(hvo, field).ToString("D");
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
                var links = data.VecProp(hvo, field).Select(Link).ToArray();
                if (metadata.GetFieldType(field) is 25 or 26) Array.Sort(links, StringComparer.Ordinal);
                return links;
            default: throw new NotSupportedException($"Unsupported field type {metadata.GetFieldType(field)} for {value.Guid}/{field}.");
        }
    }
}
