using System.Buffers.Binary;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Tests.RecordStore;

// A page walk keeps dbstat accounting available when the bundled SQLite omits its optional virtual table.
internal sealed class SqlitePageStatistics(byte[] bytes)
{
    private readonly int size = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(16)) is var s && s == 1 ? 65536 : s;
    private readonly HashSet<int> visited = [];

    public static IReadOnlyList<(string, long, long)> Read(SqliteConnection source)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-dbstat-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
            copy.Open();
            source.BackupDatabase(copy);
            var walker = new SqlitePageStatistics(File.ReadAllBytes(temporary));
            var result = new List<(string, long, long)>();
            using var command = copy.CreateCommand();
            command.CommandText = "SELECT name, rootpage FROM sqlite_schema WHERE rootpage > 0 UNION ALL SELECT 'sqlite_schema', 1 ORDER BY name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var allocation = walker.Walk(reader.GetInt32(1));
                result.Add((reader.GetString(0), allocation.Pages * walker.size, allocation.Payload));
            }
            return result;
        }
        finally { File.Delete(temporary); }
    }

    // SQLite's file format fixes cell payload fractions and overflow placement: https://sqlite.org/fileformat2.html
    private (long Pages, long Payload) Walk(int page)
    {
        Visit(page);
        long pages = 1, payload = 0;
        var start = checked((page - 1) * size);
        var header = start + (page == 1 ? 100 : 0);
        var type = bytes[header];
        if (type is not (2 or 5 or 10 or 13)) throw new InvalidDataException("Unknown SQLite b-tree page.");
        var interior = type is 2 or 5;
        var count = Read16(header + 3);
        for (var index = 0; index < count; index++)
        {
            var cell = start + Read16(header + (interior ? 12 : 8) + 2 * index);
            if (interior)
            {
                var child = Walk(Read32(cell));
                pages += child.Pages;
                payload += child.Payload;
                cell += 4;
            }
            if (type == 5) continue;
            var length = checked((int)Varint(ref cell));
            payload += length;
            if (type == 13) Varint(ref cell);
            var usable = size - bytes[20];
            var maximum = type == 13 ? usable - 35 : (usable - 12) * 64 / 255 - 23;
            if (length <= maximum) continue;
            var minimum = (usable - 12) * 32 / 255 - 23;
            var local = minimum + (length - minimum) % (usable - 4);
            if (local > maximum) local = minimum;
            var overflow = Read32(cell + local);
            while (overflow != 0)
            {
                Visit(overflow);
                pages++;
                overflow = Read32(checked((overflow - 1) * size));
            }
        }
        if (interior)
        {
            var right = Walk(Read32(header + 8));
            pages += right.Pages;
            payload += right.Payload;
        }
        return (pages, payload);
    }

    private void Visit(int page)
    {
        if (page <= 0 || page > bytes.Length / size || !visited.Add(page))
            throw new InvalidDataException("Invalid or repeated SQLite page.");
    }
    private int Read16(int position) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position, 2));
    private int Read32(int position) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4)));
    private ulong Varint(ref int position)
    {
        ulong value = 0;
        for (var index = 0; index < 9; index++)
        {
            var next = bytes[position++];
            if (index == 8) return (value << 8) | next;
            value = (value << 7) | (uint)(next & 127);
            if (next < 128) return value;
        }
        throw new InvalidDataException("Invalid SQLite varint.");
    }
}
