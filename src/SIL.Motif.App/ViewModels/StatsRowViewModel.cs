using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.App.ViewModels;

/// <summary>Statistics columns with explicit units; unprojected parser fields remain available in Details.</summary>
public sealed class StatsRowViewModel
{
    private static readonly HashSet<string> KnownColumns = new(StringComparer.Ordinal)
    {
        "kind", "label", "form", "attempts", "passes", "elapsed_ns", "time_ns", "capped", "timed_out",
    };

    private readonly string _rawText;

    public StatsRowViewModel(JsonElement row)
    {
        Word = ReadText(row, "form");
        Kind = ReadText(row, "kind") ?? (Word is null ? null : "word");
        Object = ReadText(row, "label");
        Attempts = ReadNumber(row, "attempts");
        Passes = ReadNumber(row, "passes");
        ElapsedMs = ReadNumber(row, Word is null ? "time_ns" : "elapsed_ns") / 1_000_000;
        var capped = ReadBoolean(row, "capped");
        var timedOut = ReadBoolean(row, "timed_out");
        IsIncomplete = capped == true || timedOut == true;
        CompletionStatus = Word is null ? null : IsIncomplete
            ? "INCOMPLETE — parsing did not finish (" +
                (capped == true && timedOut == true ? "step and time limits" : capped == true ? "step limit" : "time limit") + ")"
            : capped == false && timedOut == false ? "Search completed" : "Completion unavailable";
        Details = row.ValueKind == JsonValueKind.Object
            ? row.EnumerateObject()
                .Where(property => !KnownColumns.Contains(property.Name))
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>();
        _rawText = row.GetRawText();
    }

    public string? Kind { get; }
    public string? Object { get; }
    public string? Word { get; }
    public double? Attempts { get; }
    public double? Passes { get; }
    public double? ElapsedMs { get; }
    public bool IsIncomplete { get; }
    public string? CompletionStatus { get; }
    public IReadOnlyDictionary<string, JsonElement> Details { get; }

    public bool MatchesFilter(string filterText) =>
        _rawText.Contains(filterText, StringComparison.OrdinalIgnoreCase);

    public double? NumericValue(string column) => column switch
    {
        "attempts" => Attempts,
        "passes" => Passes,
        "elapsedMs" => ElapsedMs,
        _ => null,
    };

    public string? TextValue(string column) => column switch
    {
        "kind" => Kind,
        "object" => Object,
        "word" => Word,
        "completion" => CompletionStatus,
        _ => null,
    };

    private static bool? ReadBoolean(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    private static string? ReadText(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadNumber(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
