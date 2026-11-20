using System.Text;
using System.Text.Json;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

internal static class GraphComparison
{
    private const string LinkedFilesField = "6001042";

    public static IReadOnlyList<string> Differences(IReadOnlyList<GenericRecord> expected,
        IReadOnlyList<GenericRecord> actual, JsonElement schema, string? sourceRoot = null, string? destinationRoot = null)
    {
        var byId = actual.ToDictionary(r => r.Id);
        var allowRelocation = AllowsRelocation(schema);
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
                if (allowRelocation && record.ClassId == 6001 && key == LinkedFilesField &&
                    IsLinkedFilesRelocation(before, after, sourceRoot, destinationRoot)) continue;
                differences.Add($"{record.Id}/{key} differs");
            }
        }
        differences.AddRange(byId.Keys.Select(id => "Unexpected object " + id));
        return differences;
    }

    public static int WriteCanonical(string path, IEnumerable<GenericRecord> records)
    {
        using var writer = File.CreateText(path);
        var count = 0;
        foreach (var record in records)
        {
            writer.WriteLine(LcmGraphReader.Canonical(record));
            count++;
        }
        return count;
    }

    public static void AssertCanonical(string path, IEnumerable<GenericRecord> records, JsonElement schema,
        string sourceRoot, string destinationRoot, string? importedRoot = null)
    {
        using var reader = File.OpenText(path);
        var allowRelocation = AllowsRelocation(schema);
        var differences = new List<string>();
        foreach (var record in records)
        {
            var expected = reader.ReadLine();
            Assert.NotNull(expected);
            var actual = LcmGraphReader.Canonical(record);
            if (expected == actual) continue;
            using var before = JsonDocument.Parse(expected);
            using var after = JsonDocument.Parse(actual);
            var id = before.RootElement.GetProperty("guid").GetGuid();
            Assert.True(id == record.Id, $"Expected object {id}, got {record.Id}.");
            var identityChanged = before.RootElement.GetProperty("classId").GetInt32() != record.ClassId ||
                before.RootElement.GetProperty("owner").GetRawText() != after.RootElement.GetProperty("owner").GetRawText() ||
                before.RootElement.GetProperty("owningField").GetInt32() != record.OwningField;
            var expectedFields = before.RootElement.GetProperty("fields");
            var actualFields = after.RootElement.GetProperty("fields");
            var fields = expectedFields.EnumerateObject().Select(p => p.Name).Union(actualFields.EnumerateObject().Select(p => p.Name));
            var changed = fields.Where(field =>
            {
                if (!expectedFields.TryGetProperty(field, out var left) || !actualFields.TryGetProperty(field, out var right))
                    return true;
                if (left.GetRawText() == right.GetRawText()) return false;
                return !allowRelocation || record.ClassId != 6001 || field != LinkedFilesField ||
                    !(IsLinkedFilesRelocation(left, right, sourceRoot, destinationRoot) ||
                    (importedRoot is not null && IsLinkedFilesRelocation(left, right, sourceRoot, importedRoot)));
            }).ToArray();
            if (identityChanged) differences.Add("Identity/ownership differs: " + id);
            if (changed.Length > 0) differences.Add($"{id} differs (fields: {string.Join(", ", changed)})." +
                (changed.Length == 1 && changed[0] == LinkedFilesField
                    ? $" Expected {expectedFields.GetProperty(LinkedFilesField).GetRawText()} (allowed {sourceRoot}), actual {actualFields.GetProperty(LinkedFilesField).GetRawText()} (allowed {destinationRoot})."
                    : ""));
            if (differences.Count >= 30) break;
        }
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        Assert.Null(reader.ReadLine());
    }

    private static bool AllowsRelocation(JsonElement schema) => schema.GetProperty("fields").EnumerateArray().Any(f =>
        f.GetProperty("id").GetInt32() == 6001042 && f.GetProperty("name").GetString() == "LinkedFilesRootDir" &&
        f.GetProperty("owner").GetString() == "LangProject" && f.GetProperty("type").GetInt32() == 15);

    private static bool IsLinkedFilesRelocation(JsonElement before, JsonElement after, string? sourceRoot, string? destinationRoot) =>
        sourceRoot is not null && destinationRoot is not null &&
        CanonicalJson.Normalize(before) == RawPath(Path.Combine(sourceRoot, "LinkedFiles")) &&
        CanonicalJson.Normalize(after) == RawPath(Path.Combine(destinationRoot, "LinkedFiles"));

    private static string RawPath(string path) => CanonicalJson.Serialize(new
    {
        text = path, utf16le = Convert.ToBase64String(Encoding.Unicode.GetBytes(path))
    });
}
