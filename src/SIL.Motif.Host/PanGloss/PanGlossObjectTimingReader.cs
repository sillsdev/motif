using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Reads per-word object timing JSONL into the normalized facts Motif retains with an Assessment.</summary>
public static class PanGlossObjectTimingReader
{
    /// <summary>Reads valid object rows; PanGloss's metadata and incomplete lines are ignored.</summary>
    public static IReadOnlyList<PanGlossObjectTiming> ReadJsonl(string jsonl, string word, int? passes)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        var result = new List<PanGlossObjectTiming>();
        using var reader = new StringReader(jsonl);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement;
                if (row.ValueKind != JsonValueKind.Object || row.TryGetProperty("meta", out _)) continue;
                if (!TryString(row, "kind", out var kind) || !TryString(row, "label", out var label) ||
                    !row.TryGetProperty("time_ns", out var time) || !time.TryGetDouble(out var nanoseconds) || nanoseconds < 0)
                    continue;
                var attempts = row.TryGetProperty("attempts", out var value) && value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out var count) ? count : (int?)null;
                result.Add(new PanGlossObjectTiming(kind, label, word, attempts, passes, nanoseconds / 1_000_000d));
            }
            catch (JsonException)
            {
                continue;
            }
        }
        return result;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}

/// <summary>A normalized per-word timing row read from PanGloss statistics.</summary>
public sealed record PanGlossObjectTiming(
    string Kind, string Object, string Word, int? Attempts, int? Passes, double ElapsedMs);
