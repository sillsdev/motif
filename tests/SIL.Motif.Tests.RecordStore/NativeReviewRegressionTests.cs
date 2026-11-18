using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

[Collection(LcmCacheTestCollection.Name)]
public sealed class NativeReviewRegressionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-native-review-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task NativeObjectDataReferencesKeepEachWritingSystemAndRun(int kind)
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        SeededProject.Seed(cache);
        var target = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().First();
        var source = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().Last().SensesOS[0];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var data = TsStringUtils.GetObjData(target.Guid, (FwObjDataTypes)kind);
            var chars = Enumerable.Range(0, data.Length / 2).Select(i => (char)(data[i * 2] | data[i * 2 + 1] << 8)).ToArray();
            var builder = TsStringUtils.MakeString("linklink", cache.DefaultAnalWs).GetBldr();
            builder.SetStrPropValue(0, 8, (int)FwTextPropType.ktptObjData, new string(chars));
            builder.SetStrPropValue(4, 8, (int)FwTextPropType.ktptNamedStyle, "Strong");
            Assert.Equal(target.Guid, TsStringUtils.MakeString("\uFFFC", builder.GetString().get_Properties(0))
                .GetAllEmbeddedObjectGuids((FwObjDataTypes)kind).Single());
            source.Definition.set_String(cache.DefaultAnalWs, builder.GetString());
            source.Definition.set_String(cache.DefaultVernWs, builder.GetString());
        });
        var path = NewLangProjFixture.FwDataPath(root);
        var database = Path.Combine(root, "objects.sqlite");
        await LcmRecordBridge.ImportAsync(cache, path, database);
        await using var store = new SIL.Motif.RecordStore.RecordStore(database);
        var initial = await store.ReferencesAsync();
        var evidence = new ReferenceEvidence(initial.Revision, "empty", "empty", "empty", []);
        var first = (await store.ReferencesAsync(evidence)).Incoming([target.Guid]);
        Assert.True(first.NativeComplete);
        Assert.Null(first.Total);
        Assert.Equal(4, first.Native.Count(e => e.Source == source.Guid && e.Occurrence.Length != 0));
        Assert.Equal(4, first.Native.Where(e => e.Source == source.Guid && e.Occurrence.Length != 0).Select(e => e.Occurrence).Distinct().Count());
        await store.RebuildReferenceIndexAsync();
        var second = (await store.ReferencesAsync(evidence)).Incoming([target.Guid]);
        Assert.Equal(first.Native.ToArray(), second.Native.ToArray());
    }

    [Theory]
    [InlineData("guid", true, true)]
    [InlineData("local", true, true)]
    [InlineData("foreign", false, true)]
    [InlineData("unsupported", false, false)]
    [InlineData("truncated", false, false)]
    public async Task NativeBinaryPropertiesResolveLocalLinksAndQualifyUnknownEncodings(string encoding, bool linked, bool complete)
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        SeededProject.Seed(cache);
        var target = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().First();
        IStStyle source = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            source = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(source);
            var data = TsStringUtils.GetObjData(target.Guid, (FwObjDataTypes)3);
            var raw = new string(Enumerable.Range(0, data.Length / 2).Select(i => (char)(data[i * 2] | data[i * 2 + 1] << 8)).ToArray());
            if (encoding is "local" or "foreign") raw = (char)4 + "silfw://localhost/link?database=" +
                (encoding == "local" ? "this$" : "AnotherProject") + "&guid=" + target.Guid;
            if (encoding == "unsupported") raw = ((char)6).ToString();
            if (encoding == "truncated") raw = ((char)3).ToString();
            var props = TsStringUtils.MakePropsBldr();
            props.SetStrPropValue((int)FwTextPropType.ktptObjData, raw);
            source.Rules = props.GetTextProps();
        });
        var database = Path.Combine(root, "properties.sqlite");
        await LcmRecordBridge.ImportAsync(cache, NewLangProjFixture.FwDataPath(root), database);
        await using var store = new SIL.Motif.RecordStore.RecordStore(database);
        var initial = await store.ReferencesAsync();
        var result = (await store.ReferencesAsync(new(initial.Revision, "empty", "empty", "empty", []))).Incoming([target.Guid]);
        Assert.Equal(linked ? 1 : 0, result.Native.Count(e => e.Source == source.Guid && e.Occurrence == "props"));
        Assert.Equal(complete, result.NativeComplete);
        Assert.False(result.UsageComplete);
        Assert.Null(result.Total);
        Assert.Throws<InvalidOperationException>(result.RequireComplete);
    }

    [Fact]
    public async Task DestinationDatabaseSidecarsAndContentAreNeverImportedAsProjectResources()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        new FwDataProjectLoader().Save(cache);
        var source = NewLangProjFixture.FwDataPath(root);
        var directory = Path.GetDirectoryName(source)!;
        var database = Path.Combine(directory, "records.sqlite");
        await using (var blank = new SIL.Motif.RecordStore.RecordStore(database)) { }
        new ResourceContentStore(database).Put(System.Security.Cryptography.RandomNumberGenerator.GetBytes(47));
        await LcmRecordBridge.ImportAsync(cache, source, database);
        await using var store = new SIL.Motif.RecordStore.RecordStore(database);
        var records = await store.ReadAsync();
        Assert.DoesNotContain(records.Where(r => r.Kind == "resource"), record =>
            record.Values["path"].GetString()!.StartsWith("records.sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedNativeCandidateTiesPreserveTheRepositoryWinner()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var seed = SeededProject.Seed(cache);
        SeededProject.SeedText(cache, seed);
        var forms = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().Where(f => f is IMoStemAllomorph).Take(2).ToArray();
        var bundles = cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().AllInstances().Take(2).ToArray();
        Assert.Equal(2, forms.Length);
        Assert.Equal(2, bundles.Length);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var form in forms) form.Form.set_String(cache.DefaultVernWs, "[tie]");
            for (var i = 0; i < bundles.Length; i++) bundles[i].MorphRA = forms[i];
        });
        var nativeForms = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().Where(f => forms.Contains(f)).ToArray();
        var nativeBundles = cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().AllInstances().Where(b => bundles.Contains(b)).ToArray();
        var records = new LcmGraphReader(cache).Read().ToDictionary(r => r.Id);
        Assert.Equal(nativeForms[0].Guid, nativeForms.Reverse().OrderBy(f => records[f.Guid].Provenance!.RepositoryOrdinals["MoForm"]).First().Guid);
        Assert.Equal(nativeBundles[0].Guid, nativeBundles.Reverse().OrderBy(b => records[b.Guid].Provenance!.RepositoryOrdinals["WfiMorphBundle"]).First().Guid);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
