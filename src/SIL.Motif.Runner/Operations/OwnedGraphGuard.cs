using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Operations;

/// <summary>Binds deletion to the complete live owned graph and every native or custom incoming reference.</summary>
public static class OwnedGraphGuard
{
    public static ICmObject[] Members(ICmObject root) => root.AllOwnedObjects.Prepend(root).OrderBy(item => item.Guid).ToArray();

    public static string Digest(LcmCache cache, ICmObject root)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var rows = Members(root).Select(item => new
        {
            item.Guid, item.ClassName, owner = item.Owner?.Guid,
            fields = metadata.GetFields(item.ClassID, true, -1).Where(flid =>
                    metadata.GetOwnClsId(flid) != 0 && !((IFwMetaDataCache)metadata).get_IsVirtual(flid))
                .Order().Select(flid => new { flid, value = ReadValue(cache, item, flid, (CellarPropertyType)metadata.GetFieldType(flid)) }).ToArray(),
        }).ToArray();
        return Hash(JsonSerializer.Serialize(rows));
    }

    public static string ReferenceDigest(LcmCache cache, ICmObject root) => Hash(JsonSerializer.Serialize(References(cache, root)));

    public static void RequireNoExternalReferences(LcmCache cache, ICmObject root)
    {
        var references = References(cache, root);
        if (references.Length == 0) return;
        throw new InvalidOperationException("Deletion would leave incoming references: " + string.Join("; ", references.Select(row =>
            $"{row.SourceClass} {row.Source}.{row.Field}{(row.Ordinal is { } ordinal ? $"[{ordinal}]" : "")} -> {row.Target}: {Remedy(row.SourceClass)}")));
    }

    private static string Remedy(string sourceClass) => sourceClass switch
    {
        "MoAlloAdhocProhib" or "MoMorphAdhocProhib" => "use compose-edit-adhoc-prohibition-references or compose-delete-adhoc-prohibition",
        "WfiMorphBundle" => "retarget ordinary bundle references with retire-allomorph; for a process bundle, remove the analysis with remove-analysis",
        "MoDeriv" or "MoCompoundRuleApp" => "remove or retarget the stored compound/derivation reference in FieldWorks before composing deletion",
        _ => "remove or retarget this native/custom reference in FieldWorks before composing deletion",
    };

    internal static ReferenceRow[] References(LcmCache cache, ICmObject root)
    {
        var targets = Members(root).ToDictionary(item => item.Hvo);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var result = new List<ReferenceRow>();
        foreach (var source in cache.ServiceLocator.ObjectRepository.AllInstances().Where(item => !targets.ContainsKey(item.Hvo)))
        foreach (var flid in metadata.GetFields(source.ClassID, true, -1))
        {
            if (((IFwMetaDataCache)metadata).get_IsVirtual(flid)) continue;
            var type = (CellarPropertyType)metadata.GetFieldType(flid);
            var name = metadata.GetFieldName(flid);
            if (source is ILexEntry or ILexDb && name is "AllAllomorphs" or "AllPossibleAllomorphs") continue;
            if (type is CellarPropertyType.ReferenceAtomic)
                Add(cache.DomainDataByFlid.get_ObjectProp(source.Hvo, flid), null);
            else if (type is CellarPropertyType.ReferenceSequence or CellarPropertyType.ReferenceCollection)
                for (var index = 0; index < cache.DomainDataByFlid.get_VecSize(source.Hvo, flid); index++)
                    Add(cache.DomainDataByFlid.get_VecItem(source.Hvo, flid, index), type == CellarPropertyType.ReferenceSequence ? index : null);

            void Add(int target, int? ordinal)
            {
                if (targets.TryGetValue(target, out var value))
                    result.Add(new(CanonicalId.FromGuid(source.Guid).Value, source.ClassName, name, ordinal, CanonicalId.FromGuid(value.Guid).Value));
            }
        }
        return result.OrderBy(row => row.Source, StringComparer.Ordinal).ThenBy(row => row.Field, StringComparer.Ordinal)
            .ThenBy(row => row.Ordinal).ThenBy(row => row.Target, StringComparer.Ordinal).ToArray();
    }

    private static object? ReadValue(LcmCache cache, ICmObject item, int flid, CellarPropertyType type)
    {
        var data = cache.DomainDataByFlid;
        string? Identity(int hvo) => hvo == 0 ? null : CanonicalId.FromGuid(cache.ServiceLocator.ObjectRepository.GetObject(hvo).Guid).Value;
        switch (type)
        {
            case CellarPropertyType.Boolean: return data.get_BooleanProp(item.Hvo, flid);
            case CellarPropertyType.Integer: return data.get_IntProp(item.Hvo, flid);
            case CellarPropertyType.Time: return data.get_TimeProp(item.Hvo, flid);
            case CellarPropertyType.Guid: return data.get_GuidProp(item.Hvo, flid);
            case CellarPropertyType.Unicode: return data.get_UnicodeProp(item.Hvo, flid);
            case CellarPropertyType.String: return Rich(data.get_StringProp(item.Hvo, flid));
            case CellarPropertyType.MultiString:
            case CellarPropertyType.MultiUnicode:
                var multi = data.get_MultiStringProp(item.Hvo, flid);
                return Enumerable.Range(0, multi.StringCount).Select(index =>
                {
                    var text = multi.GetStringFromIndex(index, out var ws);
                    return new { ws = cache.WritingSystemFactory.GetStrFromWs(ws), value = Rich(text) };
                }).OrderBy(row => row.ws, StringComparer.Ordinal).ToArray();
            case CellarPropertyType.OwningAtomic:
            case CellarPropertyType.ReferenceAtomic: return Identity(data.get_ObjectProp(item.Hvo, flid));
            case CellarPropertyType.OwningCollection:
            case CellarPropertyType.OwningSequence:
            case CellarPropertyType.ReferenceCollection:
            case CellarPropertyType.ReferenceSequence:
                var ids = Enumerable.Range(0, data.get_VecSize(item.Hvo, flid))
                    .Select(index => Identity(data.get_VecItem(item.Hvo, flid, index))).ToArray();
                return type is CellarPropertyType.OwningCollection or CellarPropertyType.ReferenceCollection ? ids.Order(StringComparer.Ordinal).ToArray() : ids;
            default: throw new InvalidOperationException($"Cannot bind deletion of {item.ClassName}: unsupported field {flid} ({type}).");
        }
    }

    private static object? Rich(ITsString? text) => text is null ? null : new
    {
        text.Text,
        runs = Enumerable.Range(0, text.RunCount).Select(index =>
        {
            var properties = text.get_Properties(index);
            return new { text = text.get_RunText(index), integers = Enumerable.Range(0, properties.IntPropCount).Select(property =>
            {
                var variation = properties.GetIntProp(property, out var type, out var number);
                return new { type, variation, number };
            }).OrderBy(row => row.type).ToArray(), strings = Enumerable.Range(0, properties.StrPropCount).Select(property =>
            {
                var value = properties.GetStrProp(property, out var type);
                return new { type, value };
            }).OrderBy(row => row.type).ToArray() };
        }).ToArray(),
    };

    private static string Hash(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal sealed record ReferenceRow(string Source, string SourceClass, string Field, int? Ordinal, string Target);
}
