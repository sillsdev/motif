using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class DirectorReviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-director-" + Guid.NewGuid().ToString("N"));
    private string Database { get { Directory.CreateDirectory(root); return Path.Combine(root, "records.sqlite"); } }

    [Fact]
    public async Task IncomingIncludesOwnedDescendantsUnlessTheResultIsExplicitlyPartial()
    {
        var entry = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        var sense = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2, Owner = entry.Id, OwningField = 10 };
        var analysis = new GenericRecord { Id = Guid.NewGuid(), ClassId = 3 };
        entry.Values["10"] = JsonSerializer.SerializeToElement(new[] { sense.Id });
        analysis.Values["20"] = JsonSerializer.SerializeToElement(sense.Id);
        await using var store = new Store(Database);
        await ImportNative(store, [entry, sense, analysis], new Dictionary<int, int> { [10] = 25, [20] = 24 });
        var initial = await store.ReferencesAsync();
        var index = await store.ReferencesAsync(new(initial.Revision, "empty", "empty", "empty", []));
        var result = index.Incoming([entry.Id]);
        Assert.Contains(result.Native, edge => edge.Source == analysis.Id && edge.Target == sense.Id);
        var shallow = index.Incoming([entry.Id], false);
        Assert.False(shallow.NativeComplete);
        Assert.Null(shallow.NativeTotal);
        Assert.False(shallow.Complete);
        Assert.Null(shallow.Total);
        Assert.Throws<InvalidOperationException>(shallow.RequireComplete);
    }

    [Fact]
    public async Task CallerEvidenceStringsCannotProveCompleteUsageClosure()
    {
        var entry = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        await using var store = new Store(Database);
        await ImportNative(store, [entry], []);
        var initial = await store.ReferencesAsync();
        var claimed = await store.ReferencesAsync(new(initial.Revision, "caller-anchors", "caller-heads", "caller-parser", []));
        var result = claimed.Incoming([entry.Id]);
        Assert.Empty(typeof(ReferenceResult).GetConstructors());
        Assert.True(result.NativeComplete);
        Assert.False(result.UsageComplete);
        Assert.False(result.Complete);
        Assert.Null(result.Total);
        Assert.Throws<InvalidOperationException>(result.RequireComplete);
    }

    [Fact]
    public async Task ReplayVerificationFailureLeavesThePriorSnapshotsReadableAfterReopen()
    {
        var first = new GenericRecord { Id = Guid.NewGuid(), Kind = "test" };
        var second = new GenericRecord { Id = Guid.NewGuid(), Kind = "test" };
        first.Values["value"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        second.Values["value"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        var path = Database;
        await using (var store = new Store(path))
        {
            await store.ImportAsync([new(first, new Dictionary<int, int>()), new(second, new Dictionary<int, int>())], Guid.NewGuid());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Entity FROM Snapshots WHERE hex(EntityId) = $id";
            command.Parameters.AddWithValue("$id", second.Id.ToString("N").ToUpperInvariant());
            var replacement = Convert.ToHexString((byte[])command.ExecuteScalar()!);
            command.Parameters.Clear();
            command.CommandText = "CREATE TRIGGER AlterReplay AFTER INSERT ON Snapshots WHEN hex(NEW.EntityId)='" +
                first.Id.ToString("N").ToUpperInvariant() + "' BEGIN UPDATE Snapshots SET Entity=x'" + replacement + "' WHERE Id=NEW.Id; END";
            command.ExecuteNonQuery();
            await Assert.ThrowsAsync<InvalidDataException>(store.RegenerateAsync);
            var retained = (await store.ReadAsync()).ToDictionary(r => r.Id);
            Assert.Equal(first.Values["value"].GetGuid(), retained[first.Id].Values["value"].GetGuid());
            Assert.Equal(second.Values["value"].GetGuid(), retained[second.Id].Values["value"].GetGuid());
        }
        await using var reopened = new Store(path);
        Assert.Equal(2, (await reopened.ReadAsync()).Count);
    }

    [Fact]
    public void EveryStoreTexteditingDependencyIsDocumentedWithItsConsumer()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(directory.FullName, "src", "SIL.Motif.RecordStore", "SIL.Motif.RecordStore.csproj"));
        var dependencies = project.Descendants("ProjectReference").Select(e => e.Attribute("Include")!.Value)
            .Where(path => path.Contains("textediting/", StringComparison.Ordinal)).Select(Path.GetFileNameWithoutExtension).ToArray();
        Assert.NotEmpty(dependencies);
        var agents = File.ReadAllText(Path.Combine(directory.FullName, "AGENTS.md"));
        var paragraph = agents.Split("\n\n").Single(p => p.Contains("**The text editor library", StringComparison.Ordinal));
        foreach (var dependency in dependencies)
            Assert.Contains(paragraph.Split(';'), statement => statement.Contains("`SIL.Motif.RecordStore`", StringComparison.Ordinal) &&
                statement.Contains("`" + dependency + "`", StringComparison.Ordinal));
    }

    private static Task ImportNative(Store store, GenericRecord[] records, Dictionary<int, int> types)
    {
        var layouts = records.ToImmutableDictionary(r => r.ClassId, r => r.Values.Keys
            .Select(int.Parse).ToImmutableDictionary(f => f, f => types[f]));
        var coverage = NativeImportCoverage.Capture(records.Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)),
            layouts, "complete-runtime-fixture");
        return store.ImportAsync(records.Select(r => new RecordImport(r, types)), Guid.NewGuid(), coverage);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
