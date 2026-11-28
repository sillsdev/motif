using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Harmony.Core;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class SeedWireTests : IDisposable
{
    private const int Field = 1000;
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("motif-record-wire-");
    private string PathFor(string name) => Path.Combine(root.FullName, name + ".sqlite");

    [Fact]
    public async Task VersionZeroPeerRefusesSeedBeforeReceivingHistory()
    {
        await using var source = new Store(PathFor("source"));
        var record = new GenericRecord { Id = Guid.NewGuid() };
        await source.ImportNativeAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid(), Coverage([record], []));

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => source.ExportHistoryJsonAsync(0));
        Assert.Contains("model version 1", refused.Message);
        var history = await source.ExportHistoryJsonAsync(1);
        var server = JsonSerializer.Deserialize<ServerCommit[]>(history.Commits)!;
        server[0].ChangeEntities[0].AuthoredModelVersion = 0;
        await using var current = new Store(PathFor("mislabeled"));
        await Assert.ThrowsAsync<InvalidDataException>(() => current.BootstrapHistoryJsonAsync(history with { Commits = JsonSerializer.SerializeToUtf8Bytes(server) }));
        Assert.Empty(await current.CommitsAsync());
    }

    [Fact]
    public async Task EmptyAndLatePeersReplaySeedJsonWithTheSameSnapshotsAndListIdentities()
    {
        var repeated = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2 };
        var other = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2 };
        var vector = ImmutableArray.Create(repeated.Id, other.Id, repeated.Id);
        var first = new GenericRecord
        {
            Id = Guid.NewGuid(), ClassId = 1,
            Provenance = new NativeProvenance(0, "test traversal", ImmutableDictionary<int, ImmutableArray<Guid>>.Empty.Add(Field, vector))
            { RepositoryOrdinals = ImmutableDictionary<string, long>.Empty.Add("test repository", 3) }
        };
        first.Values[Field.ToString()] = JsonSerializer.SerializeToElement(vector);
        var imports = new[] { repeated, other }.Concat(Enumerable.Range(0, 501)
            .Select(_ => new GenericRecord { Id = Guid.NewGuid(), ClassId = 2 })).Prepend(first).ToArray();
        var types = ImmutableDictionary<int, int>.Empty.Add(Field, 28);
        var sourcePath = PathFor("source");
        var sourceResources = new ResourceContentStore(sourcePath);
        var resourceBytes = RandomNumberGenerator.GetBytes(257);
        var resourceHash = sourceResources.Put(resourceBytes);
        var resource = new GenericRecord { Id = Guid.NewGuid(), Kind = "resource" };
        resource.Values["hash"] = JsonSerializer.SerializeToElement(resourceHash);
        resource.Values["path"] = JsonSerializer.SerializeToElement("LinkedFiles/test.bin");
        await using var source = new Store(sourcePath);
        await source.ImportNativeAsync(imports.Select(r => new RecordImport(r, r.ClassId == 1 ? types : ImmutableDictionary<int, int>.Empty))
            .Append(new(resource, ImmutableDictionary<int, int>.Empty)), Guid.NewGuid(), Coverage(imports,
                [(1, types), (2, ImmutableDictionary<int, int>.Empty)]));

        var peerPath = PathFor("peer");
        await using var peer = new Store(peerPath);
        Assert.Equal(0L, Execute(peerPath, "SELECT count(*) FROM RecordContent"));
        var payload = await source.ExportHistoryJsonAsync(1);
        var history = JsonSerializer.Deserialize<ServerCommit[]>(payload.Commits)!;
        Assert.Equal(2, history.Length);
        Assert.All(history.SelectMany(c => c.ChangeEntities), c => Assert.Equal(1, c.AuthoredModelVersion));
        var outOfOrder = JsonSerializer.SerializeToUtf8Bytes(new[] { history[1], history[0], history[1] });
        Directory.CreateDirectory(peerPath + ".resources");
        await Assert.ThrowsAsync<FileNotFoundException>(() => peer.BootstrapHistoryJsonAsync(payload with { Commits = outOfOrder }));
        Assert.Empty(await peer.CommitsAsync());
        Assert.Equal(resourceHash, new ResourceContentStore(peerPath).Put(sourceResources.Read(resourceHash)));
        var incomplete = JsonSerializer.SerializeToUtf8Bytes(new[] { history[0] });
        await Assert.ThrowsAsync<InvalidDataException>(() => peer.BootstrapHistoryJsonAsync(payload with { Commits = incomplete }));
        Assert.Empty(await peer.CommitsAsync());
        await peer.BootstrapHistoryJsonAsync(payload with { Commits = outOfOrder });
        Assert.Equal(resourceBytes, new ResourceContentStore(peerPath).Read(resourceHash));
        await AssertEquivalent(source, peer, sourcePath, peerPath);

        var atCheckpoint = await peer.Model.GetAtCommit<GenericRecord>(history[0].Id, first.Id);
        Assert.Equal(Store.SnapshotItem(await source.Model.GetAtCommit<GenericRecord>(history[0].Id, first.Id)),
            Store.SnapshotItem(atCheckpoint));
        var client = Guid.NewGuid();
        var fromSource = (await source.ReadAsync()).Single(r => r.Id == first.Id);
        var fromPeer = (await peer.ReadAsync()).Single(r => r.Id == first.Id);
        Assert.Equal(source.Lists.CheckpointRows(fromSource, Field), peer.Lists.CheckpointRows(fromPeer, Field));
        var delete = ListAuthoring.Delete(fromSource, Field, client, 2);
        Assert.Equal(delete.Operation, ListAuthoring.Delete(fromPeer, Field, client, 2).Operation);
        await source.CommitListOperationsAsync([delete], client);
        await peer.CommitListOperationsAsync([delete], client);
        var edited = (await peer.ReadAsync()).Single(r => r.Id == first.Id);
        Assert.Equal(new[] { repeated.Id, other.Id }, peer.Lists.Read(edited, Field));
        Assert.Equal(Store.SnapshotItem((await source.ReadAsync()).Single(r => r.Id == first.Id)), Store.SnapshotItem(edited));
        await peer.RegenerateAsync();

        var latePath = PathFor("late");
        new ResourceContentStore(latePath).Put(sourceResources.Read(resourceHash));
        await using var late = new Store(latePath);
        await late.BootstrapHistoryJsonAsync(await source.ExportHistoryJsonAsync(1));
        await AssertEquivalent(source, late, sourcePath, latePath);
        await late.RegenerateAsync();
        await AssertEquivalent(source, late, sourcePath, latePath);
    }

    private static async Task AssertEquivalent(Store source, Store peer, string sourcePath, string peerPath)
    {
        Assert.Equal((await source.ReadAsync()).OrderBy(r => r.Id).Select(Store.SnapshotItem),
            (await peer.ReadAsync()).OrderBy(r => r.Id).Select(Store.SnapshotItem));
        foreach (var column in new[] { "Revision", "SnapshotDigest", "IndexDigest" })
            Assert.Equal(Execute(sourcePath, $"SELECT {column} FROM RecordStoreState"), Execute(peerPath, $"SELECT {column} FROM RecordStoreState"));
        var sourceHistory = JsonSerializer.Deserialize<ServerCommit[]>((await source.ExportHistoryJsonAsync(1)).Commits)!;
        var peerHistory = JsonSerializer.Deserialize<ServerCommit[]>((await peer.ExportHistoryJsonAsync(1)).Commits)!;
        Assert.Equal(WireDigest(sourceHistory), WireDigest(peerHistory));
    }

    private static byte[] WireDigest(ServerCommit[] commits) => SHA256.HashData(RecordContent.Canonical(
        JsonSerializer.SerializeToElement(commits.OrderBy(c => c.CompareKey).Select(c => new
        {
            c.Id, c.ClientId, c.HybridDateTime, c.Metadata, Changes = c.ChangeEntities.OrderBy(change => change.Index)
        }))));

    private static NativeImportCoverage Coverage(IEnumerable<GenericRecord> records,
        IEnumerable<(int ClassId, ImmutableDictionary<int, int> Fields)> layouts) =>
        NativeImportCoverage.Capture(records.Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)),
            layouts.ToImmutableDictionary(layout => layout.ClassId, layout => layout.Fields), "native import fixture");

    private static object? Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose() => root.Delete(recursive: true);
}
