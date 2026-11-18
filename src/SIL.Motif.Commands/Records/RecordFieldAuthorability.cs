using System.Collections.Frozen;

namespace SIL.Motif.Commands.Records;

internal static class RecordFieldAuthorability
{
    private const string Inventory = "liblcm-inventory.tsv";
    private static readonly FrozenDictionary<(string, string), string> Classifications = ReadClassifications();

    internal static string? Classification(string declaringClass, string field) =>
        Classifications.GetValueOrDefault((declaringClass, field));

    internal static bool IsReadOnly(string? classification) =>
        classification is "derived-read-only" or "internal" or "observable-not-authorable" or "runner-bookkeeping";

    private static FrozenDictionary<(string, string), string> ReadClassifications()
    {
        using var source = new StreamReader(typeof(RecordFieldAuthorability).Assembly.GetManifestResourceStream(Inventory)
            ?? throw new InvalidOperationException($"Embedded field inventory '{Inventory}' is missing."));
        var header = source.ReadLine()?.Split('\t') ??
            throw new InvalidOperationException($"Embedded field inventory '{Inventory}' is empty.");
        var declaringClass = Array.IndexOf(header, "\"Class\"");
        var field = Array.IndexOf(header, "\"Field\"");
        var classification = Array.IndexOf(header, "\"Classification\"");
        if (declaringClass < 0 || field < 0 || classification < 0)
            throw new InvalidOperationException($"Unexpected columns in '{Inventory}'.");

        var fields = new Dictionary<(string, string), string>();
        while (source.ReadLine() is { } row)
        {
            if (row.Length == 0)
                continue;
            var values = row.Split('\t');
            if (values.Length != header.Length)
                throw new InvalidOperationException($"Invalid field row in '{Inventory}'.");
            var value = values[classification].Trim('"');
            if (value.Length > 0 && values[field] != "\"\"")
                fields.Add((values[declaringClass].Trim('"'), values[field].Trim('"')), value);
        }
        return fields.ToFrozenDictionary();
    }
}
