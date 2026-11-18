using System.Collections.Immutable;
using System.Text.Json;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class ReferenceIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-references-" + Guid.NewGuid().ToString("N"));
    private string Database { get { Directory.CreateDirectory(root); return Path.Combine(root, "records.sqlite"); } }

    [Fact]
    public async Task IncomingReferencesRetainMembersDescendantsAndExternalWitnessesAtOneRevision()
    {
        var entry = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        var sense = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2, Owner = entry.Id, OwningField = 10 };
        var form = new GenericRecord { Id = Guid.NewGuid(), ClassId = 3, Owner = entry.Id, OwningField = 10 };
        var analysis = new GenericRecord { Id = Guid.NewGuid(), ClassId = 4 };
        var bundle = new GenericRecord { Id = Guid.NewGuid(), ClassId = 5, Owner = analysis.Id, OwningField = 30 };
        entry.Values["10"] = JsonSerializer.SerializeToElement(new[] { sense.Id, form.Id });
        analysis.Values["30"] = JsonSerializer.SerializeToElement(new[] { bundle.Id });
        bundle.Values["20"] = JsonSerializer.SerializeToElement(sense.Id);
        bundle.Values["21"] = JsonSerializer.SerializeToElement(new[] { form.Id, form.Id });
        var types = new Dictionary<int, int> { [10] = 25, [20] = 24, [21] = 28, [30] = 27 };
        ReferenceIndexSnapshot pinned;
        await using (var store = new Store(Database))
        {
            await ImportComplete(store, new[] { entry, sense, form, analysis, bundle }.Select(r => new RecordImport(r, types)).ToArray());
            var native = await store.ReferencesAsync();
            var incomplete = native.Incoming([entry.Id], true);
            Assert.True(incomplete.NativeComplete);
            Assert.Equal(5, incomplete.NativeTotal);
            Assert.Null(incomplete.Total);
            Assert.Throws<InvalidOperationException>(incomplete.RequireComplete);
            Assert.Equal(5, incomplete.Native.Length);
            var evidence = new ReferenceEvidence(native.Revision, "anchors", "heads", "parser", [
                new(ReferenceWitnessKind.Anchor, Guid.NewGuid().ToString(), "anchors", sense.Id),
                new(ReferenceWitnessKind.ConflictingHead, Guid.NewGuid().ToString(), "heads", form.Id, bundle.Id),
                new(ReferenceWitnessKind.ParserReading, Guid.NewGuid().ToString(), "parser", entry.Id)]);
            pinned = await store.ReferencesAsync(evidence);
            var result = pinned.Incoming([entry.Id], true);
            Assert.True(result.NativeComplete);
            Assert.Equal(5, result.NativeTotal);
            Assert.False(result.UsageComplete);
            Assert.Null(result.Total);
            Assert.Throws<InvalidOperationException>(result.RequireComplete);
            Assert.Equal(3, result.Witnesses.Length);
            Assert.Equal(new[] { 0, 1 }, result.Native.Where(e => e.Field == 21).Select(e => e.Position));
            Assert.Equal(analysis.Id, pinned.Record(bundle.Id)!.Owner);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReferencesAsync(evidence with { StoreRevision = "stale" }));
            var stale = await store.ReferencesAsync(evidence with { ParserRevision = "another-assessment" });
            Assert.Null(stale.Incoming([entry.Id], true).Total);
            await store.RebuildReferenceIndexAsync();
            var rebuilt = (await store.ReferencesAsync(evidence)).Incoming([entry.Id], true);
            Assert.Equal(result.Native.ToArray(), rebuilt.Native.ToArray());
            Assert.Equal(result.Witnesses.ToArray(), rebuilt.Witnesses.ToArray());
            Assert.Equal(result.Targets.ToArray(), rebuilt.Targets.ToArray());
        }
        await using var reopened = new Store(Database);
        Assert.Equal(pinned.Revision, (await reopened.ReferencesAsync()).Revision);
        Assert.Equal(5, pinned.Incoming([entry.Id], true).Native.Length);
        Assert.False(pinned.Incoming([Guid.NewGuid()]).NativeComplete);
    }

    [Fact]
    public async Task DanglingNativeEdgesAndUnknownNamespacesCannotEstablishExclusiveUsage()
    {
        var missing = Guid.NewGuid();
        var source = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        source.Values["20"] = JsonSerializer.SerializeToElement(missing);
        var unknown = new GenericRecord { Id = Guid.NewGuid(), Kind = "unknown-reference-contract" };
        await using var store = new Store(Database);
        await store.ImportAsync([new(source, new Dictionary<int, int> { [20] = 24 }), new(unknown, new Dictionary<int, int>())], Guid.NewGuid());
        var initial = await store.ReferencesAsync();
        var evidence = new ReferenceEvidence(initial.Revision, "empty", "empty", "empty", []);
        var index = await store.ReferencesAsync(evidence);
        var result = index.Incoming([missing]);
        Assert.Single(result.Native);
        Assert.False(result.NativeComplete);
        Assert.Null(result.NativeTotal);
        Assert.False(result.Complete);
        Assert.Null(result.Total);
        Assert.Throws<InvalidOperationException>(result.RequireComplete);
    }

    [Fact]
    public async Task OwnershipCyclesAndUnknownExternalTargetsRefuseCompleteCounts()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new GenericRecord { Id = firstId, Owner = secondId, OwningField = 40 };
        var second = new GenericRecord { Id = secondId, Owner = firstId, OwningField = 40 };
        first.Values["40"] = JsonSerializer.SerializeToElement(secondId);
        second.Values["40"] = JsonSerializer.SerializeToElement(firstId);
        var types = new Dictionary<int, int> { [40] = 23 };
        await using var store = new Store(Database);
        await store.ImportAsync([new(first, types), new(second, types)], Guid.NewGuid());
        var initial = await store.ReferencesAsync();
        var evidence = new ReferenceEvidence(initial.Revision, "anchors", "heads", "parser", [
            new(ReferenceWitnessKind.Anchor, Guid.NewGuid().ToString(), "anchors", Guid.NewGuid())]);
        var result = (await store.ReferencesAsync(evidence)).Incoming([firstId], true);
        Assert.Equal(2, result.Targets.Length);
        Assert.False(result.NativeComplete);
        Assert.Null(result.NativeTotal);
        Assert.Contains(result.Limitations, text => text.Contains("cycle", StringComparison.Ordinal));
        Assert.Contains(result.Limitations, text => text.Contains("External witness target", StringComparison.Ordinal));
        Assert.Null(result.Total);
    }

    [Fact]
    public async Task DamagedProjectionIsIncompleteUntilRebuiltAndUnpublishedImportCannotBeRead()
    {
        var parent = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        var child = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2, Owner = parent.Id, OwningField = 10 };
        parent.Values["10"] = JsonSerializer.SerializeToElement(new[] { child.Id });
        await using var store = new Store(Database);
        await ImportComplete(store, [new(parent, new Dictionary<int, int> { [10] = 25 }), new(child, new Dictionary<int, int>())]);
        var initial = await store.ReferencesAsync();
        var evidence = new ReferenceEvidence(initial.Revision, "empty", "empty", "empty", []);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM IncomingReferences";
        command.ExecuteNonQuery();
        Assert.Null((await store.ReferencesAsync(evidence)).Incoming([child.Id]).NativeTotal);
        await store.RebuildReferenceIndexAsync();
        Assert.Equal(1, (await store.ReferencesAsync(evidence)).Incoming([child.Id]).NativeTotal);
        command.CommandText = "UPDATE RecordStoreState SET Published=0";
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReferencesAsync());
    }

    private static Task ImportComplete(Store store, RecordImport[] imports)
    {
        var layouts = imports.ToImmutableDictionary(i => i.Record.ClassId, i => i.Record.Values.Keys
            .Select(int.Parse).ToImmutableDictionary(f => f, f => i.FieldTypes[f]));
        var coverage = NativeImportCoverage.Capture(imports.Select(i => new ReferenceRecord(i.Record.Id,
            i.Record.ClassId, i.Record.Owner, i.Record.OwningField)), layouts, "complete-runtime-fixture");
        return store.ImportAsync(imports, Guid.NewGuid(), coverage);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
