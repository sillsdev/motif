using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.Runner.Retirement;

/// <summary>Computes a live, identity-qualified digest of every incoming reference to allomorph forms.</summary>
public static class AllomorphRetargetCensusReader
{
    /// <summary>Returns a deterministic digest of incoming references and ordered prohibition slots.</summary>
    public static string ComputeDigest(LcmCache cache, IEnumerable<Guid> forms) => Read(cache, forms).Digest;

    internal static bool HasIncomingReferences(LcmCache cache, IEnumerable<Guid> forms) =>
        Read(cache, forms).UnmanagedReferenceCount != 0;

    private static (string Digest, int UnmanagedReferenceCount) Read(LcmCache cache, IEnumerable<Guid> forms)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(forms);

        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var targets = forms.Distinct().Order().ToArray();
        if (targets.Length == 0)
            throw new ArgumentException("At least one allomorph identity is required.", nameof(forms));
        var targetObjects = targets.Select(repository.GetObject).ToArray();
        if (targetObjects.Any(item => item is not IMoForm))
            throw new ArgumentException("Every allomorph identity must resolve to a form.", nameof(forms));
        var targetHandles = targetObjects.ToDictionary(item => item.Hvo, item => item.Guid);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var allObjects = repository.AllInstances().ToArray();
        var classes = metadata.GetClassIds().ToDictionary(metadata.GetClassName);
        var references = new List<ReferenceRow>();
        var fields = new List<FieldRow>();

        foreach (var flid in metadata.GetFieldIds())
        {
            var type = (CellarPropertyType)metadata.GetFieldType(flid);
            if (!TryCardinality(type, out var cardinality)) continue;
            var destinationName = metadata.GetDstClsName(flid);
            if (!classes.TryGetValue(destinationName, out var destinationClass)) continue;

            var acceptedTargets = targetObjects.Where(item => IsSameOrSubclass(
                metadata, item.ClassID, destinationClass)).Select(item => item.Hvo).ToHashSet();
            if (acceptedTargets.Count == 0) continue;

            var declaringClass = metadata.GetOwnClsId(flid);
            var fieldRow = new FieldRow(metadata.GetOwnClsName(flid), metadata.GetFieldName(flid),
                type.ToString(), destinationName, metadata.IsCustom(flid));
            fields.Add(fieldRow);

            foreach (var source in allObjects.Where(item => IsSameOrSubclass(metadata, item.ClassID, declaringClass)))
            {
                if (cardinality == "atomic")
                {
                    var target = cache.DomainDataByFlid.get_ObjectProp(source.Hvo, flid);
                    if (acceptedTargets.Contains(target) && targetHandles.TryGetValue(target, out var guid))
                        references.Add(new ReferenceRow(source.Guid, source.ClassName, fieldRow.Name, null, guid,
                            fieldRow.IsCustom));
                    continue;
                }

                var count = cache.DomainDataByFlid.get_VecSize(source.Hvo, flid);
                for (var index = 0; index < count; index++)
                {
                    var target = cache.DomainDataByFlid.get_VecItem(source.Hvo, flid, index);
                    if (acceptedTargets.Contains(target) && targetHandles.TryGetValue(target, out var guid))
                        references.Add(new ReferenceRow(source.Guid, source.ClassName, fieldRow.Name,
                            cardinality == "sequence" ? index : null,
                            guid, fieldRow.IsCustom));
                }
            }
        }

        var payload = JsonSerializer.Serialize(new
        {
            forms = targets,
            fields = fields.Distinct().OrderBy(item => item.DeclaringClass, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal).ToArray(),
            references = references.OrderBy(item => item.Source).ThenBy(item => item.Field, StringComparer.Ordinal)
                .ThenBy(item => item.Ordinal).ThenBy(item => item.Target).ToArray()
        });
        return ("sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            references.Count(item => !IsDerivedAllomorphCollection(item.SourceClass, item.Field)));
    }

    private static bool IsDerivedAllomorphCollection(string sourceClass, string field) =>
        sourceClass is ("LexEntry" or "LexDb") && field is ("AllAllomorphs" or "AllPossibleAllomorphs");

    private static bool IsSameOrSubclass(IFwMetaDataCacheManaged metadata, int classId, int expectedBase)
    {
        for (var current = classId; current > 0; current = metadata.GetBaseClsId(current))
            if (current == expectedBase) return true;
        return false;
    }

    private static bool TryCardinality(CellarPropertyType type, out string cardinality)
    {
        cardinality = type switch
        {
            CellarPropertyType.ReferenceAtomic or CellarPropertyType.ReferenceAtom => "atomic",
            CellarPropertyType.ReferenceCollection => "collection",
            CellarPropertyType.ReferenceSequence => "sequence",
            _ => string.Empty
        };
        return cardinality.Length != 0;
    }

    private sealed record FieldRow(string DeclaringClass, string Name, string Type, string DestinationClass, bool IsCustom);
    private sealed record ReferenceRow(Guid Source, string SourceClass, string Field, int? Ordinal, Guid Target, bool IsCustom);
}
