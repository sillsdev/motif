using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.App.ViewModels;

/// <summary>Statistics columns with explicit units; unprojected parser fields remain available in Details.</summary>
public sealed class StatsRowViewModel
{
    private static readonly HashSet<string> KnownColumns = new(StringComparer.Ordinal)
    {
        "kind", "label", "form", "attempts", "passes", "capped", "timed_out",
    };

    private readonly string _rawText;
    private readonly bool _capped;
    private readonly bool _timedOut;

    public StatsRowViewModel(JsonElement row)
    {
        Word = ReadText(row, "form");
        Kind = ReadText(row, "kind") ?? (Word is null ? null : "word");
        Object = ReadText(row, "label");
        Attempts = ReadNumber(row, "attempts");
        Passes = ReadNumber(row, "passes");
        // Only a word row's work is parser work steps; a grammar object's own work counts a different thing.
        WorkSteps = Word is null ? null : ReadNumber(row, "work");
        var capped = ReadBoolean(row, "capped");
        var timedOut = ReadBoolean(row, "timed_out");
        _capped = capped == true;
        _timedOut = timedOut == true;
        IsIncomplete = _capped || _timedOut;
        SearchFinished = capped == false && timedOut == false ? true : IsIncomplete ? false : null;
        CompletionStatus = Word is null ? null : IsIncomplete
            ? capped == true && timedOut == true ? MotifAppStrings.StatsIncompleteBoth
                : capped == true ? MotifAppStrings.StatsIncompleteSearch : MotifAppStrings.StatsIncompleteTime
            : capped == false && timedOut == false ? MotifAppStrings.StatsSearchCompleted : MotifAppStrings.StatsCompletionUnavailable;
        Details = row.ValueKind == JsonValueKind.Object
            ? row.EnumerateObject()
                .Where(property => !KnownColumns.Contains(property.Name) && !(Word is not null && property.Name == "work"))
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>();
        _rawText = row.GetRawText();
    }

    public string? Kind { get; }
    public string? Object { get; }
    public string? Word { get; }
    public double? Attempts { get; }
    public double? Passes { get; }
    /// <summary>The parser work steps PanGloss recorded for a word, or null for a grammar object or an uncounted word.</summary>
    public double? WorkSteps { get; }
    public bool IsIncomplete { get; }
    /// <summary>Whether this statistics invocation's parse finished for the word.</summary>
    internal bool? SearchFinished { get; }
    public string? CompletionStatus { get; }

    /// <summary>The completion in a word or two for the table; <see cref="CompletionStatus"/> is its tooltip.</summary>
    public string CompletionShort => CompletionStatus is null ? string.Empty
        : !IsIncomplete ? (SearchFinished == true ? MotifAppStrings.StatsCompleted : MotifAppStrings.StatsNoResult)
        : _capped && _timedOut ? MotifAppStrings.StatsSearchAndTimeLimits
        : _capped ? MotifAppStrings.StatsSearchLimit
        : MotifAppStrings.StatsTimeLimit;

    public bool HasCompletion => CompletionShort.Length > 0;

    /// <summary>A search stopped at a limit wears the Stopped mark; a finished one says so in words alone.</summary>
    public Mark? CompletionMark => IsIncomplete ? Mark.Stopped : null;

    /// <summary>The numbers as the table shows them, grouped for reading; empty where the parser gave none.</summary>
    public string AttemptsText => Format(Attempts, "N0");
    public string PassesText => Format(Passes, "N0");
    /// <summary>The work steps as the table shows them, or a note that none were recorded.</summary>
    public string WorkText => WorkSteps is { } work ? work.ToString("N0", CultureInfo.CurrentCulture) : MotifAppStrings.WorkStepsNotRecorded;

    /// <summary>How strongly each number is shaded against the largest in its current scope.</summary>
    public double AttemptsHeat { get; private set; }
    public double PassesHeat { get; private set; }
    public double WorkHeat { get; private set; }

    internal void ShadeAgainst(double largestAttempts, double largestPasses, double largestWork)
    {
        AttemptsHeat = HeatOf(Attempts, largestAttempts);
        PassesHeat = HeatOf(Passes, largestPasses);
        WorkHeat = HeatOf(WorkSteps, largestWork);
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
        "workSteps" => WorkSteps,
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
