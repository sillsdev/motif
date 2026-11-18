using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIL.Harmony.Config;
using SIL.Harmony.Db;
using SIL.Harmony.Entities;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

internal sealed class RecordDatabase(DbContextOptions<RecordDatabase> options, IOptions<HarmonyConfig> config)
    : DbContext(options), ICrdtDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.UseCrdt(config.Value);
        builder.Entity<ObjectSnapshot>().Property(x => x.Entity).HasColumnType("blob")
            .HasConversion(x => Encode(x), x => Decode(x));
    }

    private static byte[] Encode(IObjectBase record)
    {
        using var output = new MemoryStream();
        using (var compression = new BrotliStream(output, CompressionLevel.Fastest, true))
            JsonSerializer.Serialize(compression, (GenericRecord)record);
        return output.ToArray();
    }

    private static IObjectBase Decode(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var compression = new BrotliStream(input, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<GenericRecord>(compression) ?? throw new InvalidDataException("Missing record.");
    }
}
