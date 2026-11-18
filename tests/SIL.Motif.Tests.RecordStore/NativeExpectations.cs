using SIL.LCModel;
using SIL.LCModel.Application;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.TextEditing.Markdown;
using SIL.LCModel.Infrastructure;
using SIL.Motif.RecordStore;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

internal sealed class NativeExpectations
{
    private readonly List<NativeReference> edges = [];
    private readonly Dictionary<Guid, (long Ordinal, Dictionary<int, Guid[]> Vectors)> provenance = new();
    private readonly Dictionary<string, Guid[]> repositories = new(StringComparer.Ordinal);

    public NativeExpectations(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var data = (ISilDataAccessManaged)cache.DomainDataByFlid;
        var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().ToArray();
        repositories.Add("MoForm", cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().Select(o => o.Guid).ToArray());
        repositories.Add("WfiMorphBundle", cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().AllInstances().Select(o => o.Guid).ToArray());
        for (var ordinal = 0; ordinal < objects.Length; ordinal++)
        {
            var source = objects[ordinal];
            var vectors = new Dictionary<int, Guid[]>();
            foreach (var field in metadata.GetFields(source.ClassID, true, -1).Where(f => !metadata.get_IsVirtual(f)))
            {
                var type = metadata.GetFieldType(field);
                if (type == 13) Text(data.get_StringProp(source.Hvo, field), "");
                if (type is 14 or 16)
                {
                    var multi = data.get_MultiStringProp(source.Hvo, field);
                    for (var alternative = 0; alternative < multi.StringCount; alternative++)
                    {
                        var text = multi.GetStringFromIndex(alternative, out var ws);
                        Text(text, "ws:" + Uri.EscapeDataString(cache.ServiceLocator.WritingSystemManager.Get(ws).Id) + "/");
                    }
                }
                if (type == 9 && source.GetType().GetProperty(metadata.GetFieldName(field))?.GetValue(source) is ITsTextProps properties)
                    Props(properties, "props");

                void Text(ITsString? text, string prefix)
                {
                    if (text is null) return;
                    for (var run = 0; run < text.RunCount; run++) Props(text.get_Properties(run), prefix + "run:" + run);
                }

                void Props(ITsTextProps properties, string occurrence)
                {
                    var raw = properties.GetStrPropValue((int)FwTextPropType.ktptObjData);
                    if (string.IsNullOrEmpty(raw)) return;
                    Guid? target = null;
                    if ((int)raw[0] is 3 or 5 or 7 or 8)
                        target = TsStringUtils.MakeString("\uFFFC", properties).GetAllEmbeddedObjectGuids((FwObjDataTypes)raw[0]).Single();
                    else if ((int)raw[0] == 4 && SilfwUrl.TryParse(raw[1..], out var url) &&
                        (url.Database is null || url.Database == SilfwUrl.CurrentProject || url.Database == cache.ProjectId.Name))
                        target = url.Guid;
                    if (target.HasValue) edges.Add(new NativeReference(source.Guid, field, -1, target.Value, false, occurrence));
                }
                if (type is not (23 or 24 or 25 or 26 or 27 or 28)) continue;
                var ids = type is 23 or 24 ? new[] { data.get_ObjectProp(source.Hvo, field) } : data.VecProp(source.Hvo, field);
                if (type >= 25) vectors.Add(field, ids.Select(h => cache.ServiceLocator.GetObject(h).Guid).ToArray());
                for (var position = 0; position < ids.Length; position++)
                    if (ids[position] != 0) edges.Add(new NativeReference(source.Guid, field, type is 23 or 24 ? -1 : position,
                        cache.ServiceLocator.GetObject(ids[position]).Guid, type is 23 or 25 or 27));
            }
            provenance.Add(source.Guid, (ordinal, vectors));
        }
    }

    public void Check(IReadOnlyList<GenericRecord> imported, ReferenceIndexSnapshot index)
    {
        var native = imported.Where(r => r.Kind == "lcm").ToDictionary(r => r.Id);
        Assert.Equal(provenance.Count, native.Count);
        foreach (var (id, expected) in provenance)
        {
            var actual = Assert.IsType<NativeProvenance>(native[id].Provenance);
            Assert.Equal(expected.Ordinal, actual.EncounterOrdinal);
            Assert.False(string.IsNullOrWhiteSpace(actual.TraversalVersion));
            foreach (var (field, vector) in expected.Vectors)
                Assert.Equal(vector, actual.Vectors.GetValueOrDefault(field, []));
            Assert.Equal(expected.Vectors.Count(v => v.Value.Length > 0), actual.Vectors.Count);
        }
        foreach (var (repository, ids) in repositories)
            for (var ordinal = 0; ordinal < ids.Length; ordinal++)
                Assert.Equal(ordinal, native[ids[ordinal]].Provenance!.RepositoryOrdinals[repository]);
        var incoming = index.Incoming(native.Keys);
        Assert.True(incoming.NativeComplete);
        Assert.Equal(Sort(edges), Sort(incoming.Native));
        Assert.DoesNotContain(incoming.Limitations, limitation => !limitation.Contains("coverage is unavailable", StringComparison.Ordinal));
    }

    private static NativeReference[] Sort(IEnumerable<NativeReference> values) => values.OrderBy(e => e.Source)
        .ThenBy(e => e.Field).ThenBy(e => e.Position).ThenBy(e => e.Target).ThenBy(e => e.Occurrence, StringComparer.Ordinal).ToArray();
}
