using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed partial class DeletionSafetyTests
{
    [RequiresSymbolicLinkFact]
    public async Task FileAndParentDirectoryAliasesShareTheDatabaseWriterAndResources()
    {
        await Import();
        var alias = Path.Combine(root, "alias.sqlite");
        var parentAlias = Path.Combine(root, "linked-parent");
        File.CreateSymbolicLink(alias, Database);
        Directory.CreateSymbolicLink(parentAlias, root);
        var viaParent = Path.Combine(parentAlias, Path.GetFileName(Database));
        using (File.Open(Database + ".write.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(async () => { await using var store = new Store(alias); });
            await Assert.ThrowsAsync<IOException>(async () => { await using var store = new Store(viaParent); });
        }
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(63);
        var hash = new ResourceContentStore(Database).Put(bytes);
        Assert.Equal(bytes, new ResourceContentStore(alias).Read(hash));
        Assert.Equal(bytes, new ResourceContentStore(viaParent).Read(hash));
    }

    [Theory]
    [InlineData("missing-class")]
    [InlineData("swapped-roles")]
    [InlineData("custom-bundles")]
    [InlineData("wrong-bundle-destination")]
    [InlineData("wrong-reference-destination")]
    public async Task InconsistentSemanticMetadataCannotProveNativeWarningCounts(string defect)
    {
        var seed = await Import();
        IReadOnlyList<GenericRecord> records;
        await using (var original = new Store(Database)) records = await original.ReadAsync();
        var manifest = records.Single(r => r.Kind == "manifest");
        var schema = JsonNode.Parse(manifest.Values["schema"].GetRawText())!;
        var classes = schema["classes"]!.AsArray();
        var fields = schema["fields"]!.AsArray();
        var types = fields.ToDictionary(f => f!["id"]!.GetValue<int>(), f => f!["type"]!.GetValue<int>());
        var bundleField = fields.Single(f => f!["owner"]!.GetValue<string>() == "WfiAnalysis" && f["name"]!.GetValue<string>() == "MorphBundles")!;
        var entryClass = classes.Single(c => c!["name"]!.GetValue<string>() == "LexEntry")!["id"]!.GetValue<int>();
        if (defect == "missing-class") classes.Remove(classes.Single(c => c!["name"]!.GetValue<string>() == "WfiWordform"));
        else if (defect == "swapped-roles")
        {
            var wordform = classes.Single(c => c!["name"]!.GetValue<string>() == "WfiWordform")!;
            var gloss = classes.Single(c => c!["name"]!.GetValue<string>() == "WfiGloss")!;
            wordform["name"] = "WfiGloss";
            gloss["name"] = "WfiWordform";
        }
        else if (defect == "custom-bundles") bundleField["custom"] = true;
        else if (defect == "wrong-bundle-destination") bundleField["destination"] = entryClass;
        else fields.Single(f => f!["owner"]!.GetValue<string>() == "WfiMorphBundle" && f["name"]!.GetValue<string>() == "Sense")!["destination"] = entryClass;
        manifest.Values["schema"] = JsonSerializer.SerializeToElement(schema);
        var coverage = records.Single(r => r.Kind == "native-coverage").Values["basis"].Deserialize<NativeImportCoverage>()!;
        await using var changed = new Store(Path.Combine(root, "changed.sqlite"));
        await changed.ImportAsync(records.Where(r => r.Kind != "native-coverage").Select(r => new RecordImport(r, types)), Guid.NewGuid(), coverage);
        var index = await changed.ReferencesAsync();
        Assert.True(index.Incoming([seed.FirstEntryId]).NativeComplete);
        var impact = index.DeletionImpact(seed.FirstEntryId);
        Assert.False(impact.NativeComplete);
        Assert.Null(impact.NativeAnalysisCount);
        Assert.Null(impact.NativeBundleCount);
        Assert.Null(impact.NativeLinkCount);
        Assert.Throws<IncompleteDeletionClosureException>(() => impact.RequireDeletionAllowed(impact.PlanToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationDuringReadReturnsOneCoherentPoint(bool references)
    {
        var seed = await Import();
        using var pause = new PublicationReadPause(Database);
        await using var reader = new Store(Database);
        await using var writer = new Store(Database);
        var revision = (await writer.ReferencesAsync()).Revision;
        var old = (await writer.ReadAsync()).Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()];
        pause.Arm();
        var reading = Task.Run(async () => references ? (object)await reader.ReferencesAsync() : await reader.ReadAsync());
        try
        {
            await Wait.UntilAsync(() => pause.Paused.IsCompleted || reading.IsCompleted, "Reader reached its revision query", detail: () => pause.Detail);
            Assert.True(pause.Paused.IsCompleted, pause.Detail);
            await writer.SetFieldAsync(seed.FirstEntryId, LexEntryTags.kflidHomographNumber, old,
                JsonSerializer.SerializeToElement(old.GetInt32() + 1), revision, Guid.NewGuid());
        }
        finally { pause.Release(); }
        var result = await reading;
        if (references)
        {
            var pinned = Assert.IsType<ReferenceIndexSnapshot>(result);
            Assert.Equal(revision, pinned.Revision);
            Assert.Equal(9, pinned.DeletionImpact(seed.FirstEntryId).NativeLinkCount);
        }
        else
        {
            var rows = Assert.IsAssignableFrom<IReadOnlyList<GenericRecord>>(result);
            Assert.Equal(old.GetInt32(), rows.Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()].GetInt32());
        }
    }

    private sealed class PublicationReadPause : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string path;
        private readonly IDisposable all;
        private readonly List<IDisposable> subscriptions = [];
        private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> seen = new();
        public string Detail => string.Join(" | ", seen);
        private int armed;
        private int used;
        private bool disposed;
        public Task Paused => paused.Task;
        public PublicationReadPause(string path)
        {
            this.path = Store.ResolvePath(path);
            all = DiagnosticListener.AllListeners.Subscribe(this);
        }
        void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
        {
            if (listener.Name != "Microsoft.EntityFrameworkCore") return;
            lock (subscriptions)
                if (!disposed) subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
        }
        void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is not CommandExecutedEventData data || Volatile.Read(ref armed) == 0) return;
            if (data.Command.CommandText.Contains("Hash", StringComparison.Ordinal))
                seen.Enqueue(data.Command.Connection?.DataSource + ": " + data.Command.CommandText);
            if (data.Command.Connection?.DataSource != path ||
                !data.Command.CommandText.Contains("\"Hash\"", StringComparison.Ordinal) ||
                !data.Command.CommandText.Contains("\"Commits\"", StringComparison.Ordinal) || Interlocked.Exchange(ref used, 1) != 0) return;
            paused.TrySetResult();
            released.Task.GetAwaiter().GetResult();
        }
        public void Arm() => Volatile.Write(ref armed, 1);
        public void Release() => released.TrySetResult();
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            Release();
            lock (subscriptions)
            {
                disposed = true;
                foreach (var subscription in subscriptions) subscription.Dispose();
                subscriptions.Clear();
            }
            all.Dispose();
        }
    }
}
