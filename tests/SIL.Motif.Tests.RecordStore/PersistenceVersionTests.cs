using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class PersistenceVersionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-versions-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("snapshot", "Unknown record codec version.")]
    [InlineData("content-envelope", "Unknown content envelope.")]
    [InlineData("content", "Unknown content version.")]
    [InlineData("layout", "Unknown initialization layout.")]
    public async Task FuturePersistenceVersionsAreRefusedByTheirOwnVersionGuard(string layer, string message)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "records.sqlite");
        var record = new GenericRecord { Id = Guid.NewGuid(), Kind = "test" };
        record.Values["value"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        await using (var store = new Store(path))
            await store.ImportAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid());
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        if (layer == "snapshot")
        {
            command.CommandText = "SELECT Entity FROM Snapshots LIMIT 1";
            var bytes = (byte[])command.ExecuteScalar()!;
            bytes[0] = 2;
            command.CommandText = "UPDATE Snapshots SET Entity=$bytes";
            command.Parameters.AddWithValue("$bytes", bytes);
        }
        else
        {
            var rows = ReadContent(connection);
            var selected = rows[0];
            var bytes = selected.Data.ToArray();
            if (layer == "content-envelope") bytes[0] = 2;
            else if (layer == "content")
            {
                var raw = Unpack(bytes);
                raw[0] = 3;
                bytes = [0, ..raw];
            }
            else
            {
                var version = Encoding.UTF8.GetBytes("version");
                byte[] marker = [0, (byte)version.Length, ..version, 3, 4];
                selected = Assert.Single(rows, row => Unpack(row.Data).AsSpan().EndsWith(marker));
                var raw = Unpack(selected.Data);
                raw[^1] = 6;
                bytes = [0, ..raw];
            }
            command.CommandText = "UPDATE RecordContent SET Data=$bytes WHERE Id=$id";
            command.Parameters.AddWithValue("$bytes", bytes);
            command.Parameters.AddWithValue("$id", selected.Id);
        }
        command.ExecuteNonQuery();
        if (layer == "layout")
        {
            // A valid content root isolates the layout guard from ordinary corruption refusal.
            command.Parameters.Clear();
            command.CommandText = "UPDATE RecordStoreState SET ContentDigest=$digest";
            command.Parameters.AddWithValue("$digest", ContentRoot(ReadContent(connection)));
            command.ExecuteNonQuery();
        }
        InvalidDataException error;
        if (layer is "snapshot" or "layout")
        {
            await using var reopened = new Store(path);
            error = await Assert.ThrowsAsync<InvalidDataException>(() => reopened.ReadAsync());
        }
        else error = Assert.Throws<InvalidDataException>(() => new Store(path));
        Assert.Equal(message, error.Message);
    }

    private static (int Id, byte[] Data)[] ReadContent(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Data FROM RecordContent ORDER BY Id";
        using var reader = command.ExecuteReader();
        var rows = new List<(int, byte[])>();
        while (reader.Read()) rows.Add((reader.GetInt32(0), (byte[])reader.GetValue(1)));
        return rows.ToArray();
    }

    private static byte[] Unpack(byte[] bytes)
    {
        if (bytes[0] == 0) return bytes[1..];
        using var input = new MemoryStream(bytes, 1, bytes.Length - 1);
        using var compression = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        compression.CopyTo(output);
        return output.ToArray();
    }

    private static string ContentRoot((int Id, byte[] Data)[] rows)
    {
        using var stream = new MemoryStream();
        foreach (var row in rows)
        {
            var id = (uint)row.Id;
            while (id >= 128) { stream.WriteByte((byte)(id | 128)); id >>= 7; }
            stream.WriteByte((byte)id);
            var hash = SHA256.HashData(row.Data);
            stream.WriteByte((byte)hash.Length);
            stream.Write(hash);
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
