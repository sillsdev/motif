using System.Text;
using System.Text.Json;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;

namespace SIL.Motif.Tests.RecordStore;

internal static class GraphComparison
{
    public static IReadOnlyList<string> Differences(IReadOnlyList<GenericRecord> expected,
        IReadOnlyList<GenericRecord> actual, JsonElement schema, string? sourceRoot = null, string? destinationRoot = null)
    {
        var byId = actual.ToDictionary(r => r.Id);
        var allowRelocation = schema.GetProperty("fields").EnumerateArray().Any(f =>
            f.GetProperty("id").GetInt32() == 6001042 && f.GetProperty("name").GetString() == "LinkedFilesRootDir" &&
            f.GetProperty("owner").GetString() == "LangProject" && f.GetProperty("type").GetInt32() == 15);
        var differences = new List<string>();
        foreach (var record in expected)
        {
            if (!byId.Remove(record.Id, out var other)) { differences.Add("Missing object " + record.Id); continue; }
            if (record.ClassId != other.ClassId || record.Owner != other.Owner || record.OwningField != other.OwningField)
                differences.Add("Identity/ownership differs: " + record.Id);
            foreach (var key in record.Values.Keys.Union(other.Values.Keys))
            {
                if (!record.Values.TryGetValue(key, out var before) || !other.Values.TryGetValue(key, out var after))
                {
                    differences.Add($"{record.Id}/{key} differs");
                    continue;
                }
                if (CanonicalJson.Normalize(before) == CanonicalJson.Normalize(after)) continue;
                if (allowRelocation && record.ClassId == 6001 && key == "6001042" && sourceRoot is not null && destinationRoot is not null &&
                    CanonicalJson.Normalize(before) == RawPath(Path.Combine(sourceRoot, "LinkedFiles")) &&
                    CanonicalJson.Normalize(after) == RawPath(Path.Combine(destinationRoot, "LinkedFiles"))) continue;
                differences.Add($"{record.Id}/{key} differs");
            }
        }
        differences.AddRange(byId.Keys.Select(id => "Unexpected object " + id));
        return differences;
    }

    private static string RawPath(string path) => CanonicalJson.Serialize(new
    {
        text = path, utf16le = Convert.ToBase64String(Encoding.Unicode.GetBytes(path))
    });
}
