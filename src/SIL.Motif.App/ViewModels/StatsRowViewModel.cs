using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One row of a <c>stats</c> JSON-rows query, projecting the known columns (<see cref="Kind"/>,
/// <see cref="Object"/>, <see cref="Word"/>, <see cref="Attempts"/>, <see cref="Failures"/>,
/// <see cref="Elapsed"/>) when PanGloss's row carries them, while <see cref="Details"/> retains every
/// other property verbatim. A PanGloss release that adds a field is never dropped: it simply has no
/// dedicated grid column yet, and still appears in <see cref="Details"/>.
/// </summary>
public sealed class StatsRowViewModel
{
    private static readonly HashSet<string> KnownColumns =
        new(StringComparer.Ordinal) { "kind", "object", "word", "attempts", "failures", "elapsed" };

    private readonly string _rawText;

    public StatsRowViewModel(JsonElement row)
    {
        Kind = ReadText(row, "kind");
        Object = ReadText(row, "object");
        Word = ReadText(row, "word");
        Attempts = ReadNumber(row, "attempts");
        Failures = ReadNumber(row, "failures");
        Elapsed = ReadNumber(row, "elapsed");
        Details = row.ValueKind == JsonValueKind.Object
            ? row.EnumerateObject()
                .Where(property => !KnownColumns.Contains(property.Name))
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>();
        _rawText = row.GetRawText();
    }

    public string? Kind { get; }

    public string? Object { get; }

    public string? Word { get; }

    public double? Attempts { get; }

    public double? Failures { get; }

    public double? Elapsed { get; }

    /// <summary>Every property PanGloss's row carried that none of the known columns name.</summary>
    public IReadOnlyDictionary<string, JsonElement> Details { get; }

    /// <summary>Whether this row's raw JSON text contains <paramref name="filterText"/>, ordinal case-insensitively.</summary>
    public bool MatchesFilter(string filterText) =>
        _rawText.Contains(filterText, StringComparison.OrdinalIgnoreCase);

    /// <summary>The named known numeric column's value, or <c>null</c> when the name is not one of them.</summary>
    public double? NumericValue(string column) => column switch
    {
        "attempts" => Attempts,
        "failures" => Failures,
        "elapsed" => Elapsed,
        _ => null,
    };

    /// <summary>The named known text column's value, or <c>null</c> when the name is not one of them.</summary>
    public string? TextValue(string column) => column switch
    {
        "kind" => Kind,
        "object" => Object,
        "word" => Word,
        _ => null,
    };

    private static string? ReadText(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    // Parsed as a double either way, so a numeric string never sorts by raw, lexicographic text.
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
