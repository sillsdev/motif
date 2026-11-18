using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

public sealed record NativeImportCoverage(int Version, string AdapterVersion, string IdentityDigest,
    ImmutableDictionary<int, int> ClassCounts, ImmutableDictionary<int, ImmutableDictionary<int, int>> Layouts, string? SchemaDigest = null)
{
    public static NativeImportCoverage Capture(IEnumerable<ReferenceRecord> nativeRecords,
        ImmutableDictionary<int, ImmutableDictionary<int, int>> layouts, string adapterVersion, JsonElement? schema = null)
    {
        var records = nativeRecords.ToArray();
        return new(1, adapterVersion, Identities(records), records.GroupBy(r => r.ClassId)
            .ToImmutableDictionary(g => g.Key, g => g.Count()), layouts, schema.HasValue ? ComputeSchemaDigest(schema.Value) : null);
    }

    internal static string ComputeSchemaDigest(JsonElement schema) =>
        Convert.ToHexStringLower(SHA256.HashData(RecordContent.Canonical(schema)));

    internal static string Identities(IEnumerable<ReferenceRecord> records)
    {
        using var output = new BinaryOutput();
        output.Byte(1);
        foreach (var record in records.OrderBy(r => r.Id)) { output.Guid(record.Id); output.Integer(record.ClassId); }
        return Convert.ToHexStringLower(SHA256.HashData(output.Finish()));
    }
}
