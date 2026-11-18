using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Application;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.RecordStore.Lcm;

internal static class LcmGraphWriter
{
    public static void Restore(string path, JsonElement schema, IReadOnlyList<GenericRecord> records)
    {
        FwDataProjectLoader.Init();
        var destination = Path.GetDirectoryName(path)!;
        var templates = Path.Combine(Path.GetDirectoryName(destination)!, "templates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(templates);
        try
        {
            using var cache = new FwDataProjectLoader().CreateBlankProject(path, "en", "en", templates);
            var defaults = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().ToList();
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var root in defaults.Where(o => o.Owner == null))
                    if (root.IsValidObject) cache.DomainDataByFlid.DeleteObj(root.Hvo);
            });
            cache.ActionHandlerAccessor.Commit();
            Populate(cache, schema, records);
            Save(cache);
        }
        finally { Directory.Delete(templates, true); }
    }

    private static void Populate(LcmCache cache, JsonElement schema, IReadOnlyList<GenericRecord> records)
    {
        var fields = schema.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetInt32());
        var classes = schema.GetProperty("classes").EnumerateArray().ToDictionary(c => c.GetProperty("id").GetInt32(), c => c.GetProperty("name").GetString()!);
        var objects = new Dictionary<Guid, ICmObject>();
        var tags = new HashSet<string>(StringComparer.Ordinal) { "en" };
        foreach (var record in records)
        foreach (var (key, value) in record.Values)
        {
            var kind = fields[int.Parse(key, CultureInfo.InvariantCulture)].GetProperty("type").GetInt32();
            if (kind is 14 or 16) foreach (var alternative in value.EnumerateObject()) tags.Add(alternative.Name);
            CollectTags(value, tags);
        }
        foreach (var tag in tags) cache.ServiceLocator.WritingSystemManager.GetOrSet(tag, out _);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var field in fields.Values.Where(f => f.GetProperty("custom").GetBoolean()))
            {
                var description = new FieldDescription(cache)
                {
                    Class = cache.MetaDataCacheAccessor.GetClassId(field.GetProperty("owner").GetString()),
                    Name = field.GetProperty("name").GetString(), Type = (CellarPropertyType)field.GetProperty("type").GetInt32(),
                    DstCls = field.GetProperty("destination").GetInt32(), WsSelector = field.GetProperty("ws").GetInt32(),
                    ListRootId = field.GetProperty("listRoot").GetGuid()
                };
                if (field.GetProperty("label").GetString() is { } label) description.Userlabel = label;
                if (field.GetProperty("help").GetString() is { } help) description.HelpString = help;
                if (field.GetProperty("xml").GetString() is { } xml) description.XmlUI = xml;
                description.UpdateCustomField();
                if (description.Id != field.GetProperty("id").GetInt32())
                    throw new NotSupportedException("Custom-field IDs require an explicit remap for this schema.");
            }
            var setup = cache.ServiceLocator.GetInstance<IDataSetup>();
            var nextHvo = setup.GetType().GetMethod("GetNextRealHvo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
            var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            var register = typeof(LcmCache).Assembly.GetType("SIL.LCModel.ICmObjectRepositoryInternal")!.GetMethod("RegisterObjectAsCreated")!;
            foreach (var record in records)
            {
                var type = typeof(LcmCache).Assembly.GetType("SIL.LCModel.DomainImpl." + classes[record.ClassId])!;
                var value = (ICmObject)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                    [cache, (int)nextHvo.Invoke(setup, null)!, record.Id], CultureInfo.InvariantCulture)!;
                objects.Add(record.Id, value);
                register.Invoke(repository, [value]);
            }
            var loadingType = typeof(LcmCache).Assembly.GetType("SIL.LCModel.Infrastructure.LoadingServices")!;
            var loading = ((IServiceProvider)cache.ServiceLocator).GetService(loadingType)!;
            foreach (var record in records)
            {
                var row = new XElement("rt", new XAttribute("class", classes[record.ClassId]), new XAttribute("guid", record.Id));
                if (record.Owner is Guid owner) row.Add(new XAttribute("ownerguid", owner));
                foreach (var (key, value) in record.Values)
                {
                    var field = fields[int.Parse(key, CultureInfo.InvariantCulture)];
                    var kind = field.GetProperty("type").GetInt32();
                    if (kind < 23) continue;
                    var custom = field.GetProperty("custom").GetBoolean();
                    var element = new XElement(custom ? "Custom" : field.GetProperty("name").GetString()!);
                    if (custom) element.Add(new XAttribute("name", field.GetProperty("name").GetString()!));
                    var ids = kind is 23 or 24 ? value.ValueKind == JsonValueKind.Null ? [] : new[] { value.GetGuid() }
                        : value.EnumerateArray().Select(v => v.GetGuid()).ToArray();
                    foreach (var id in ids) element.Add(new XElement("objsur", new XAttribute("guid", id), new XAttribute("t", kind is 23 or 25 or 27 ? "o" : "r")));
                    row.Add(element);
                }
                new InternalLcmObject(objects[record.Id]).Invoke("LoadFromDataStore",
                    [typeof(LcmCache), typeof(XElement), loadingType], [cache, row, loading]);
            }
            foreach (var multiPhase in new[] { false, true })
            foreach (var record in records)
            foreach (var (key, value) in record.Values)
            {
                var field = int.Parse(key, CultureInfo.InvariantCulture);
                var kind = fields[field].GetProperty("type").GetInt32();
                if (field <= 105 || kind >= 23 || (kind is 14 or 16) != multiPhase) continue;
                try { Set(cache, objects[record.Id], field, kind, value); }
                catch (Exception error) { throw new InvalidDataException($"Cannot restore {classes[record.ClassId]}.{fields[field].GetProperty("name").GetString()} ({record.Id}/{field}).", error); }
            }
        });
    }

    private static void CollectTags(JsonElement value, HashSet<string> tags)
    {
        if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CollectTags(item, tags);
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == "ints")
                foreach (var item in property.Value.EnumerateArray())
                    if (item.GetProperty("value").ValueKind == JsonValueKind.String &&
                        !int.TryParse(item.GetProperty("value").GetString(), out _)) tags.Add(item.GetProperty("value").GetString()!);
            CollectTags(property.Value, tags);
        }
    }

    private static void Set(LcmCache cache, ICmObject value, int field, int kind, JsonElement stored)
    {
        var target = new InternalLcmObject(value);
        switch (kind)
        {
            case 1: target.Set(field, stored.GetBoolean()); break;
            case 2: target.Set(field, stored.GetInt32()); break;
            case 6: target.Set(field, stored.GetGuid()); break;
            case 5: target.Set(field, new DateTime(long.Parse(stored.GetProperty("ticks").GetString()!, CultureInfo.InvariantCulture), Enum.Parse<DateTimeKind>(stored.GetProperty("kind").GetString()!))); break;
            case 8: target.Set(field, new GenDate(Enum.Parse<GenDate.PrecisionType>(stored.GetProperty("precision").GetString()!), stored.GetProperty("month").GetInt32(), stored.GetProperty("day").GetInt32(), stored.GetProperty("year").GetInt32(), stored.GetProperty("isAD").GetBoolean())); break;
            case 15: target.Set(field, LcmText.RestoreRaw(stored)); break;
            case 13: target.Set(field, LcmText.RestoreText(cache, stored)); break;
            case 14:
            case 16:
                var accessor = target.Invoke("GetITsMultiStringProperty", [typeof(int)], [field])!;
                var setter = typeof(LcmCache).Assembly.GetType("SIL.LCModel.IMultiAccessorInternal")!.GetMethod("SetAltQuietly")!;
                foreach (var alternative in stored.EnumerateObject())
                    setter.Invoke(accessor, [cache.ServiceLocator.WritingSystemManager.Get(alternative.Name).Handle, LcmText.RestoreText(cache, alternative.Value)]);
                break;
            case 9:
                if (stored.TryGetProperty("textProps", out var properties)) target.Set(field, LcmText.RestoreProperties(cache, properties));
                else target.Set(field, stored.GetProperty("base64").ValueKind == JsonValueKind.Null ? null : Convert.FromBase64String(stored.GetProperty("base64").GetString()!));
                break;
            default: throw new NotSupportedException("Unsupported LCM field type " + kind);
        }
    }

    public static void Save(LcmCache cache)
    {
        cache.ActionHandlerAccessor.Commit();
        var method = typeof(LcmCache).GetMethod("SaveAndForceNewestXmlForCmObjectWithoutUnitOfWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(cache, [null, new object[] { cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().ToList() }]);
    }
}

// Bulk restoration avoids edit-time factories that synthesize extra owned objects and side effects.
internal sealed class InternalLcmObject(ICmObject value)
{
    private static readonly Type Contract = typeof(LcmCache).Assembly.GetType("SIL.LCModel.ICmObjectInternal")!;
    public object? Invoke(string name, Type[] types, object?[] arguments) => Contract.GetMethod(name, types)!.Invoke(value, arguments);
    public void Set<T>(int field, T stored) => Invoke("SetProperty", [typeof(int), typeof(T), typeof(bool)], [field, stored, false]);
}
