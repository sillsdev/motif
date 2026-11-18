using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.RecordStore;

internal sealed record StorageMeasurement(long Main, long Wal, long Assets,
    IReadOnlyList<(string Name, long Bytes, long Payload)> Tables)
{
    public long Total => Main + Wal + Assets;

    public static StorageMeasurement Read(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        var tables = SqlitePageStatistics.Read(connection);
        return new StorageMeasurement(new FileInfo(path).Length, Length(path + "-wal"),
            Directory.Exists(path + ".resources") ? Directory.EnumerateFiles(path + ".resources", "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length) : 0, tables);
    }

    public void Write(ITestOutputHelper output, string point)
    {
        output.WriteLine($"Storage {point}: main={Main}; WAL={Wal}; assets={Assets}; total={Total}");
        output.WriteLine("dbstat name | allocated bytes | payload bytes");
        foreach (var table in Tables) output.WriteLine($"{table.Name} | {table.Bytes} | {table.Payload}");
    }

    private static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
