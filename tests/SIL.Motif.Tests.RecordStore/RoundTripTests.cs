using System.Text.Json;
using System.Security.Cryptography;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.WritingSystems;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Application;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.RecordStore;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RoundTripTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GeneratedProjectSurvivesStoreReopenAndLcmSerialization()
    {
        Directory.CreateDirectory(root);
        var projectRoot = Path.Combine(root, "Temp");
        using (var cache = NewLangProjFixture.CreateCache(projectRoot))
        {
            var seed = SeededProject.Seed(cache);
            SeededProject.SeedText(cache, seed);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToArray();
                var variant = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var reference = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
                variant.EntryRefsOS.Add(reference);
                reference.RefType = LexEntryRefTags.krtVariant;
                reference.ComponentLexemesRS.Add(entries[0]);
                Assert.Empty(variant.SensesOS);
            });
            new FwDataProjectLoader().Save(cache);
        }
        var source = NewLangProjFixture.FwDataPath(projectRoot);
        var resource = Path.Combine(Path.GetDirectoryName(source)!, "SharedSettings", "opaque.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(resource)!);
        File.WriteAllBytes(resource, RandomNumberGenerator.GetBytes(57));
        await Check(source);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task IndonesianCopySurvivesStoreReopenAndLcmSerialization() =>
        await Check(CopyProject(Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_INDONESIAN")!), true);

    [PrivateProjectFact("MOTIF_RECORD_STORE_CEBUANO")]
    public async Task CebuanoCopySurvivesStoreReopenAndLcmSerialization() =>
        await Check(CopyProject(Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_CEBUANO")!));

    [Fact]
    public void GraphReaderCapturesNativeTraversalBeforeCanonicalSorting()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        SeededProject.Seed(cache);
        var graph = new LcmGraphReader(cache);
        var records = graph.Read().ToDictionary(r => r.Id);
        var native = cache.ServiceLocator.GetInstance<SIL.LCModel.ICmObjectRepository>().AllInstances().ToArray();
        var data = (ISilDataAccessManaged)cache.DomainDataByFlid;
        for (var ordinal = 0; ordinal < native.Length; ordinal++)
        {
            var item = native[ordinal];
            var provenance = Assert.IsType<NativeProvenance>(records[item.Guid].Provenance);
            Assert.Equal(ordinal, provenance.EncounterOrdinal);
            Assert.Contains(typeof(LcmCache).Assembly.ManifestModule.ModuleVersionId.ToString("D"), provenance.TraversalVersion);
            foreach (var (field, type) in graph.FieldTypes.Where(p => p.Value >= 25))
            {
                if (!records[item.Guid].Values.ContainsKey(field.ToString())) continue;
                var expected = data.VecProp(item.Hvo, field).Select(hvo => cache.ServiceLocator.GetObject(hvo).Guid);
                Assert.Equal(expected, provenance.Vectors.GetValueOrDefault(field, []));
            }
        }
    }

    [Fact]
    public async Task ImportRefusesUnsavedChangesToAnExistingWritingSystem()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        new FwDataProjectLoader().Save(cache);
        cache.ServiceLocator.WritingSystemManager.Get("en").DefaultFont = new FontDefinition(Guid.NewGuid().ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => LcmRecordBridge.ImportAsync(cache,
            NewLangProjFixture.FwDataPath(root), Path.Combine(root, "dirty.sqlite")));
    }

    [Fact]
    public void SourceAssetGateDetectsDroppedWritingSystemsResourcesAndChangedBytes()
    {
        var directory = Path.Combine(root, "assets");
        Directory.CreateDirectory(directory);
        var bytes = RandomNumberGenerator.GetBytes(61);
        var tag = "en";
        File.WriteAllBytes(Path.Combine(directory, "opaque.bin"), bytes);
        var source = new SourceAssets(directory, [tag]);
        var ws = new GenericRecord { Id = Guid.NewGuid(), Kind = "writing-system" };
        ws.Values["tag"] = JsonSerializer.SerializeToElement(tag);
        ws.Values["path"] = JsonSerializer.SerializeToElement("WritingSystemStore/en.ldml");
        ws.Values["ldml"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(bytes));
        var resource = new GenericRecord { Id = Guid.NewGuid(), Kind = "resource" };
        resource.Values["path"] = JsonSerializer.SerializeToElement("opaque.bin");
        var database = Path.Combine(root, "assets.sqlite");
        var content = new ResourceContentStore(database);
        resource.Values["hash"] = JsonSerializer.SerializeToElement(content.Put(bytes));
        source.AssertImported([ws, resource], database);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => source.AssertImported([resource], database));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => source.AssertImported([ws], database));
        resource.Values["hash"] = JsonSerializer.SerializeToElement(content.Put(RandomNumberGenerator.GetBytes(61)));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => source.AssertImported([ws, resource], database));
    }

    private string CopyProject(string source)
    {
        Assert.True(File.Exists(source), "Private gate input does not exist: " + source);
        var directory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        var destination = Path.Combine(root, Path.GetFileName(directory));
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Contains(".motif.", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, Path.GetRelativePath(directory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return Path.Combine(destination, Path.GetFileName(source));
    }

    private async Task Check(string source, bool indonesian = false)
    {
        var database = Path.Combine(root, "records.sqlite");
        IReadOnlyList<GenericRecord> before;
        JsonElement schema;
        SourceAssets assets;
        NativeExpectations native;
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadScratchCache(source, Path.Combine(root, "templates")))
        {
            var graph = new LcmGraphReader(cache);
            before = graph.Read();
            native = new NativeExpectations(cache);
            schema = graph.Schema();
            if (indonesian)
            {
                Assert.Equal(16068, before.Count);
                var classes = schema.GetProperty("classes").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("id").GetInt32());
                Assert.Equal(54, before.Count(r => r.ClassId == classes["WfiAnalysis"]));
                Assert.Equal(123, before.Count(r => r.ClassId == classes["WfiMorphBundle"]));
            }
            assets = new SourceAssets(Path.GetDirectoryName(source)!, cache.ServiceLocator.WritingSystemManager.WritingSystems.Select(ws => ws.Id));
            await LcmRecordBridge.ImportAsync(cache, source, database);
        }
        IReadOnlyList<GenericRecord> imported;
        await using (var store = new SIL.Motif.RecordStore.RecordStore(database))
        {
            imported = await store.ReadAsync();
            assets.AssertImported(imported, database);
            native.Check(imported, await store.ReferencesAsync());
            var initial = StorageMeasurement.Read(database);
            initial.Write(output, "after import/reopen");
            if (indonesian) Assert.InRange(initial.Total, 1, 16_500_000);
            await store.RegenerateAsync();
            var replayed = await store.ReadAsync();
            Assert.Equal(imported.OrderBy(r => r.Id).Select(LcmGraphReader.Canonical),
                replayed.OrderBy(r => r.Id).Select(LcmGraphReader.Canonical));
            native.Check(replayed, await store.ReferencesAsync());
            imported = replayed;
            Assert.Equal(before.Select(LcmGraphReader.Canonical), imported.Where(r => r.Kind == "lcm")
                .OrderBy(r => r.Id.ToString("D"), StringComparer.Ordinal).Select(LcmGraphReader.Canonical));
            var commits = await store.CommitsAsync();
            Assert.All(commits, c =>
            {
                Assert.InRange(c.ChangeCount, 1, 500);
                Assert.Equal(TimeSpan.Zero, c.UtcTime.Offset);
                Assert.Equal(0, c.UtcTime.Ticks % 10);
            });
            output.WriteLine($"Objects={before.Count}; classes={before.Select(r => r.ClassId).Distinct().Count()}; commits={commits.Count}; changes={commits.Sum(c => c.ChangeCount)}; storeBytes={new FileInfo(database).Length}");
        }
        var measurement = StorageMeasurement.Read(database);
        measurement.Write(output, "after full history replay");
        if (indonesian) Assert.InRange(measurement.Total, 1, 16_500_000);
        var destination = Path.Combine(root, "restored");
        var exported = await LcmRecordBridge.ExportAsync(database, destination);
        using var reopened = loader.LoadScratchCache(exported, Path.Combine(root, "templates"));
        var restored = new LcmGraphReader(reopened);
        var after = restored.Read();
        Assert.Equal(CanonicalJson.Normalize(schema), CanonicalJson.Normalize(restored.Schema()));
        Assert.Equal(before.Count, after.Count);
        var differences = GraphComparison.Differences(before, after, schema, Path.GetDirectoryName(source), destination);
        assets.AssertExported(destination);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Take(30)));
        foreach (var record in imported.Where(r => r.Kind is "writing-system" or "resource"))
        {
            var bytes = record.Kind == "writing-system"
                ? Convert.FromBase64String(record.Values["ldml"].GetString()!)
                : new ResourceContentStore(database).Read(record.Values["hash"].GetString()!);
            Assert.Equal(bytes, File.ReadAllBytes(LcmRecordBridge.ResourcePath(destination, record.Values["path"].GetString()!)));
        }
        output.WriteLine($"Canonical equality: {after.Count} objects, zero unexempted differences; writing systems={imported.Count(r => r.Kind == "writing-system")}; resources={imported.Count(r => r.Kind == "resource")}");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

public sealed class PrivateProjectFactAttribute : FactAttribute
{
    public PrivateProjectFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = "Private copy gate: set " + variable + " to an external .fwdata copy; never commit project data.";
    }
}
