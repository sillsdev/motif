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

    public Guid DeletionTarget { get; }
    private readonly Guid[] deletionAnalyses;
    private readonly Guid[] deletionBundles;
    private readonly NativeReference[] deletionLinks;

    public NativeExpectations(LcmCache cache, bool highestFanIn = false)
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
        DeletionTarget = highestFanIn ? HighestFanIn(objects) :
            (objects.OfType<IWfiMorphBundle>().Select(b => FindEntry(b.MorphRA) ?? FindEntry(b.SenseRA) ?? FindEntry(b.MsaRA))
                .FirstOrDefault(e => e is not null) ?? objects.OfType<ILexEntry>().First()).Guid;
        var subtree = objects.Where(o => InSubtree(o, DeletionTarget)).Select(o => o.Guid).ToHashSet();
        deletionLinks = edges.Where(e => !e.Owning && subtree.Contains(e.Target) && !subtree.Contains(e.Source)).ToArray();
        var analyses = new HashSet<Guid>();
        var bundles = new HashSet<Guid>();
        foreach (var source in deletionLinks.Select(e => e.Source).Distinct())
        {
            ICmObject? item = cache.ServiceLocator.GetObject(source);
            while (item is not null)
            {
                if (item is IWfiMorphBundle) bundles.Add(item.Guid);
                if (item is IWfiAnalysis) { analyses.Add(item.Guid); break; }
                item = item.Owner;
            }
        }
        deletionAnalyses = analyses.Order().ToArray();
        deletionBundles = bundles.Order().ToArray();
    }

    private Guid HighestFanIn(ICmObject[] objects)
    {
        var byId = objects.ToDictionary(o => o.Guid);
        var counts = objects.Where(o => o is ILexEntry or ILexSense).ToDictionary(o => o.Guid, _ => 0);
        foreach (var edge in edges)
        {
            if (edge.Owning || !byId.TryGetValue(edge.Target, out var target)) continue;
            for (ICmObject? item = target; item is not null; item = item.Owner)
                if (counts.TryGetValue(item.Guid, out var count) && !InSubtree(byId[edge.Source], item.Guid))
                    counts[item.Guid] = count + 1;
        }
        return counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
    }

    private static bool InSubtree(ICmObject? item, Guid root)
    {
        for (; item is not null; item = item.Owner)
            if (item.Guid == root) return true;
        return false;
    }

    private static ILexEntry? FindEntry(ICmObject? item)
    {
        while (item is not null && item is not ILexEntry) item = item.Owner;
        return item as ILexEntry;
    }

    public void CheckDeletion(ReferenceIndexSnapshot index)
    {
        var impact = index.DeletionImpact(DeletionTarget);
        Assert.True(impact.NativeComplete, string.Join("; ", impact.Limitations));
        Assert.Equal(deletionAnalyses, impact.Analyses.ToArray());
        Assert.Equal(deletionBundles, impact.Bundles.ToArray());
        Assert.Equal(Sort(deletionLinks), Sort(impact.Links));
        Assert.Equal(deletionAnalyses.Length, impact.NativeAnalysisCount);
        Assert.Equal(deletionBundles.Length, impact.NativeBundleCount);
        Assert.Equal(deletionLinks.Length, impact.NativeLinkCount);
        Assert.False(impact.Complete);
        Assert.Null(impact.AnalysisCount);
        Assert.Null(impact.LinkCount);
        Assert.Throws<IncompleteDeletionClosureException>(() => impact.RequireDeletionAllowed(impact.PlanToken));
    }

    public void Check(IReadOnlyList<GenericRecord> imported, ReferenceIndexSnapshot index)
    {
        CheckReferences(index);
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
    }

    public void CheckReferences(ReferenceIndexSnapshot index)
    {
        CheckDeletion(index);
        var incoming = index.Incoming(provenance.Keys);
        Assert.True(incoming.NativeComplete, string.Join("; ", incoming.Limitations));
        Assert.Equal(Sort(edges), Sort(incoming.Native));
        Assert.DoesNotContain(incoming.Limitations, limitation => !limitation.Contains("coverage is unavailable", StringComparison.Ordinal));
    }

    private static NativeReference[] Sort(IEnumerable<NativeReference> values) => values.OrderBy(e => e.Source)
        .ThenBy(e => e.Field).ThenBy(e => e.Position).ThenBy(e => e.Target).ThenBy(e => e.Occurrence, StringComparer.Ordinal).ToArray();
}
