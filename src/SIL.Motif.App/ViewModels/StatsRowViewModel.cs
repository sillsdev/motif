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

    /// <summary>The completion in a word or two for the table; <see cref="CompletionStatus"/> is its tooltip.</summary>
    public string CompletionShort => CompletionStatus is null ? string.Empty
        : !IsIncomplete ? (CompletionStatus == "Search completed" ? "Completed" : "Unknown")
        : CompletionStatus.Contains("step and time", StringComparison.Ordinal) ? "Step and time limits"
        : CompletionStatus.Contains("step", StringComparison.Ordinal) ? "Step limit" : "Time limit";

    public bool HasCompletion => CompletionShort.Length > 0;

    /// <summary>The shared meaning of the completion: finished, or stopped at a limit.</summary>
    public Verdict CompletionMeaning => IsIncomplete ? Verdict.Limit : CompletionShort == "Completed" ? Verdict.Agrees : Verdict.New;

    /// <summary>The numbers as the table shows them, grouped for reading; empty where the parser gave none.</summary>
    public string AttemptsText => Format(Attempts, "N0");
    public string PassesText => Format(Passes, "N0");
    public string ElapsedText => Format(ElapsedMs, ElapsedMs is < 10 ? "N2" : "N0");

    /// <summary>How strongly each number is shaded against the largest in its column, set by the grid's owner.</summary>
    public double AttemptsHeat { get; private set; }
    public double PassesHeat { get; private set; }
    public double ElapsedHeat { get; private set; }

    internal void ShadeAgainst(double largestAttempts, double largestPasses, double largestElapsed)
    {
        AttemptsHeat = HeatOf(Attempts, largestAttempts);
        PassesHeat = HeatOf(Passes, largestPasses);
        ElapsedHeat = HeatOf(ElapsedMs, largestElapsed);
    }

    // The same scale Try a Word's effort table uses, so a shade means the same thing in both.
    private static double HeatOf(double? value, double largest)
    {
        var intensity = TraceEffortViewModel.Intensity(value ?? 0, largest);
        return intensity == 0 ? 0 : 0.12 + 0.58 * intensity;
    }

    private static string Format(double? value, string format) =>
        value is { } number ? number.ToString(format, CultureInfo.CurrentCulture) : string.Empty;
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
