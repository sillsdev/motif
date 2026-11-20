using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Application;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

[Collection(LcmCacheTestCollection.Name)]
public sealed class BuiltInLayoutTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-layout-" + Guid.NewGuid().ToString("N"));

    private static string TablePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new FileNotFoundException("Repository root not found."),
            "src", "SIL.Motif.RecordStore", "BuiltInLayouts.g.cs");
    }

    [Fact]
    public void CheckedInTableIsRegeneratedFromPinnedLcmMetadata()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var generated = LcmLayoutTable.Generate((IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor);
        // Set MOTIF_REGENERATE_LAYOUT_TABLE=1 after an intentional LCM upgrade, then pin the new digest below.
        if (Environment.GetEnvironmentVariable("MOTIF_REGENERATE_LAYOUT_TABLE") == "1") File.WriteAllText(TablePath(), generated);
        Assert.Equal(generated, File.ReadAllText(TablePath()).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TableContentAndMetadataDigestArePinned()
    {
        Assert.Equal("05a8b5b7515ec89505e6aa434e913ac82a5738696f5a0e921492b2f54c523fb7", BuiltInLayouts.TableDigest);
        Assert.Equal("69c48830b739b691c7db85390c659f7713b2b27812e7c17478111c1495880482", BuiltInLayouts.MetadataDigest);
        Assert.Equal(BuiltInLayouts.Version, "lcm-builtin/" + BuiltInLayouts.MetadataDigest[..16]);
        Assert.InRange(BuiltInLayouts.Count, 1, 100_000);
    }

    [Fact]
    public void ImportUnderDifferentBuiltInMetadataIsRefused()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var description = LcmLayoutTable.Describe(metadata).ToList();
        LcmLayoutTable.Require(description);
        var changed = description.Select((line, index) => index == description.Count / 2 ? line + " 1" : line).ToList();
        Assert.Contains("differs from the pinned layout table",
            Assert.Throws<InvalidDataException>(() => LcmLayoutTable.Require(changed)).Message);
        Assert.Throws<InvalidDataException>(() => LcmLayoutTable.Require(description.Where(line => !line.StartsWith("field 101 ")).ToList()));
    }

    [Fact]
    public async Task LayoutsNameTheTableVersionAndEmbedOnlyFieldsTheTableCannotAnswerFor()
    {
        Directory.CreateDirectory(root);
        const int homograph = LexEntryTags.kflidHomographNumber;
        const int custom = 9_999_999;
        var record = new GenericRecord { Id = Guid.NewGuid(), ClassId = LexEntryTags.kClassId };
        record.Values[homograph.ToString()] = JsonSerializer.SerializeToElement(3);
        record.Values[custom.ToString()] = JsonSerializer.SerializeToElement(4);
        await using var store = new Store(Path.Combine(root, "records.sqlite"));
        await store.ImportAsync([new(record, new Dictionary<int, int> { [homograph] = 2, [custom] = 2 })], Guid.NewGuid());
        var read = Assert.Single(await store.ReadAsync());
        Assert.Equal(BuiltInLayouts.Version, read.Layout.GetProperty("table").GetString());
        var types = read.Layout.GetProperty("types");
        Assert.False(types.TryGetProperty(homograph.ToString(), out _));
        Assert.Equal(2, types.GetProperty(custom.ToString()).GetInt32());
        Assert.Equal(2, RecordInitialization.Type(read, homograph.ToString()));
        Assert.Equal(2, RecordInitialization.Type(read, custom.ToString()));
    }

    [Fact]
    public void CustomFieldIdentityIgnoresTheNumericFlidAndTracksTheDefinition()
    {
        var identity = LcmLayoutTable.CustomIdentity("LexEntry", "Note", 14, 0);
        Assert.Equal(identity, LcmLayoutTable.CustomIdentity("LexEntry", "Note", 14, 0));
        Assert.NotEqual(identity, LcmLayoutTable.CustomIdentity("LexSense", "Note", 14, 0));
        Assert.NotEqual(identity, LcmLayoutTable.CustomIdentity("LexEntry", "Notes", 14, 0));
        Assert.NotEqual(identity, LcmLayoutTable.CustomIdentity("LexEntry", "Note", 16, 0));
        Assert.NotEqual(identity, LcmLayoutTable.CustomIdentity("LexEntry", "Note", 14, 5008));
    }

    [Fact]
    public async Task ExportRemapsCustomFieldsIntoAProjectWhoseFlidsDiffer()
    {
        var (source, original, schema) = await ImportSeeded("source");
        var custom = schema.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("custom").GetBoolean()).ToArray();
        Assert.NotEmpty(custom);
        Assert.All(custom, f => Assert.Equal(LcmLayoutTable.CustomIdentity(f.GetProperty("owner").GetString()!,
            f.GetProperty("name").GetString()!, f.GetProperty("type").GetInt32(), f.GetProperty("destination").GetInt32()),
            f.GetProperty("identity").GetString()));
        var shifted = await Reimport(source, "shifted", offset: 1_000_000, tamper: null);
        var exported = await LcmRecordBridge.ExportAsync(shifted, Path.Combine(root, "restored"));
        using var cache = new FwDataProjectLoader().LoadScratchCache(exported, Path.Combine(root, "templates"));
        var restored = new LcmGraphReader(cache);
        Assert.Equal(CanonicalJson.Normalize(schema), CanonicalJson.Normalize(restored.Schema()));
        var differences = GraphComparison.Differences(original, restored.Read(), schema);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Take(20)));
    }

    [Fact]
    public async Task ExportRebindsCustomOwningFieldsAndTheirChildren()
    {
        const string name = "OwnedTexts";
        var (source, original, schema) = await ImportSeeded("source", seeded =>
        {
            var field = new FieldDescription(seeded)
            {
                Class = LexEntryTags.kClassId, Name = name, Userlabel = name,
                Type = CellarPropertyType.OwningSequence, DstCls = StTextTags.kClassId
            };
            field.UpdateCustomField();
            var entry = seeded.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().First();
            for (var index = 0; index < 2; index++)
                seeded.DomainDataByFlid.MakeNewObject(StTextTags.kClassId, entry.Hvo, field.Id, index);
        });
        var fieldId = schema.GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();
        var children = original.Where(r => r.OwningField == fieldId).OrderBy(r => r.Values["105"].GetInt32()).ToArray();
        Assert.Equal(2, children.Length);

        const int offset = 1_000_000;
        var shifted = await Reimport(source, "shifted", offset, tamper: null);
        await using (var store = new Store(shifted))
        {
            var stored = (await store.ReadAsync()).ToDictionary(r => r.Id);
            Assert.All(children, child =>
            {
                Assert.Equal(fieldId + offset, stored[child.Id].OwningField);
                Assert.Equal(fieldId + offset, stored[child.Id].Values["104"].GetInt32());
            });
        }

        var exported = await LcmRecordBridge.ExportAsync(shifted, Path.Combine(root, "restored"));
        using var cache = new FwDataProjectLoader().LoadScratchCache(exported, Path.Combine(root, "templates"));
        var restored = new LcmGraphReader(cache);
        var actual = restored.Read();
        var actualField = cache.MetaDataCacheAccessor.GetFieldId2(LexEntryTags.kClassId, name, true);
        Assert.NotEqual(fieldId + offset, actualField);
        var parent = cache.ServiceLocator.GetObject(children[0].Owner!.Value);
        var data = (ISilDataAccessManaged)cache.DomainDataByFlid;
        Assert.Equal(children.Select(child => child.Id), data.VecProp(parent.Hvo, actualField)
            .Select(hvo => cache.ServiceLocator.GetObject(hvo).Guid));
        foreach (var expected in children)
        {
            var child = Assert.Single(actual, r => r.Id == expected.Id);
            Assert.Equal(parent.Guid, child.Owner);
            Assert.Equal(actualField, child.OwningField);
            Assert.Equal(actualField, child.Values["104"].GetInt32());
            Assert.Equal(actualField, data.get_IntProp(cache.ServiceLocator.GetObject(child.Id).Hvo, 104));
        }
        Assert.Equal(CanonicalJson.Normalize(schema), CanonicalJson.Normalize(restored.Schema()));
        var differences = GraphComparison.Differences(original, actual, schema);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Take(20)));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("renamed")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public async Task ExportNamesTheCustomFieldWhoseIdentityIsWrongRenamedRepeatedOrMissing(string tamper)
    {
        var (source, _, schema) = await ImportSeeded("source");
        var first = schema.GetProperty("fields").EnumerateArray().First(f => f.GetProperty("custom").GetBoolean());
        var identity = first.GetProperty("identity").GetString()!;
        var tampered = await Reimport(source, tamper, offset: 0, tamper);
        var error = await Assert.ThrowsAsync<CustomFieldIdentityException>(() => LcmRecordBridge.ExportAsync(tampered, Path.Combine(root, "refused")));
        if (tamper == "missing") Assert.Contains("custom field is missing", error.Message);
        else if (tamper == "duplicate") { Assert.Equal(identity, error.Identity); Assert.Contains(identity, error.Message); }
        else
        {
            Assert.Contains("renamed or edited", error.Message);
            Assert.NotNull(error.Identity);
        }
    }

    private async Task<(string Store, IReadOnlyList<GenericRecord> Graph, JsonElement Schema)> ImportSeeded(
        string name, Action<LcmCache>? customize = null)
    {
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "Temp");
        using (var seeded = NewLangProjFixture.CreateCache(project))
        {
            SeededProject.Seed(seeded);
            if (customize is not null) NonUndoableUnitOfWorkHelper.Do(seeded.ActionHandlerAccessor, () => customize(seeded));
            new FwDataProjectLoader().Save(seeded);
        }
        var path = NewLangProjFixture.FwDataPath(project);
        using var cache = new FwDataProjectLoader().LoadScratchCache(path, Path.Combine(root, "source-templates"));
        var graph = new LcmGraphReader(cache);
        var records = graph.Read();
        var schema = graph.Schema();
        var store = Path.Combine(root, name + ".sqlite");
        await LcmRecordBridge.ImportAsync(cache, path, store);
        return (store, records, schema);
    }

    // Rewrites a stored project as if its custom fields had been numbered differently, then stores that.
    private async Task<string> Reimport(string source, string name, int offset, string? tamper)
    {
        IReadOnlyList<GenericRecord> records;
        await using (var original = new Store(source)) records = await original.ReadAsync();
        var manifest = records.Single(r => r.Kind == "manifest");
        var schema = JsonNode.Parse(manifest.Values["schema"].GetRawText())!;
        var fields = schema["fields"]!.AsArray();
        var moved = new Dictionary<int, int>();
        foreach (var field in fields.Where(f => f!["custom"]!.GetValue<bool>()))
        {
            var id = field!["id"]!.GetValue<int>();
            moved[id] = id + offset;
            field["id"] = id + offset;
        }
        if (tamper == "wrong") fields.First(f => f!["custom"]!.GetValue<bool>())!["identity"] = "00";
        if (tamper == "renamed") fields.First(f => f!["custom"]!.GetValue<bool>())!["name"] = "Renamed";
        if (tamper == "missing") fields.Remove(fields.First(f => f!["custom"]!.GetValue<bool>()));
        if (tamper == "duplicate")
        {
            var first = fields.First(f => f!["custom"]!.GetValue<bool>())!;
            var copy = JsonNode.Parse(first.ToJsonString())!;
            copy["id"] = first["id"]!.GetValue<int>() + 7;
            fields.Add(copy);
        }
        var types = fields.ToDictionary(f => f!["id"]!.GetValue<int>(), f => f!["type"]!.GetValue<int>());
        var moves = records.Select(record =>
        {
            var provenance = record.Provenance;
            if (offset != 0 && provenance is not null)
                provenance = provenance with
                {
                    Vectors = provenance.Vectors.ToImmutableDictionary(
                        vector => moved.GetValueOrDefault(vector.Key, vector.Key), vector => vector.Value)
                };
            var copy = new GenericRecord
            {
                Id = record.Id, Kind = record.Kind, ClassId = record.ClassId, Owner = record.Owner,
                OwningField = moved.GetValueOrDefault(record.OwningField, record.OwningField), Provenance = provenance
            };
            foreach (var (key, value) in record.Values)
                copy.Values[record.Kind == "lcm" && moved.TryGetValue(int.Parse(key), out var to) ? to.ToString() : key] = value;
            if (copy.OwningField != record.OwningField)
                copy.Values["104"] = JsonSerializer.SerializeToElement(copy.OwningField);
            if (record.Kind == "manifest") copy.Values["schema"] = JsonSerializer.SerializeToElement(schema);
            return new RecordImport(copy, record.Kind == "lcm" ? types : new Dictionary<int, int>());
        }).ToList();
        var path = Path.Combine(root, name + ".sqlite");
        await using (var target = new Store(path)) await target.ImportAsync(moves.Where(m => m.Record.Kind != "native-coverage"), Guid.NewGuid());
        CopyDirectory(source + ".resources", path + ".resources");
        return path;
    }

    private static void CopyDirectory(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
