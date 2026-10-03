using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

[JsonConverter(typeof(JsonStringEnumConverter<WritingSystemKind>))]
public enum WritingSystemKind
{
    [JsonStringEnumMemberName("vernacular")] Vernacular,
    [JsonStringEnumMemberName("analysis")] Analysis,
}

/// <summary>
/// Display settings from the saved project, with sizes in points, never device units. An id may occur in both
/// current lists; Kind, Position and IsDefault describe that list membership. Position counts from zero.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WritingSystemDisplay(
    string Id, string Name, string Abbreviation, WritingSystemKind Kind, int Position, bool IsDefault,
    string FontFamily, string FontFeatures, bool RightToLeft,
    IReadOnlyDictionary<string, double> StyleSizes)
{
    /// <summary>Effective font family and verbatim features for each key in StyleSizes.</summary>
    public IReadOnlyDictionary<string, WritingSystemStyleFont> StyleFonts { get; init; } =
        new Dictionary<string, WritingSystemStyleFont>();
}

/// <summary>Requested font after style and writing-system overrides; no installed-font substitution.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WritingSystemStyleFont(string FontFamily, string FontFeatures);

/// <summary>Null WritingSystem explicitly means composed text, non-language text, or no known single system.</summary>
public sealed record WritingSystemText(string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WritingSystem);

/// <summary>The current Baseline's display settings, without opening the FieldWorks project.</summary>
public sealed record WritingSystemsResponse(bool HasBaseline, IReadOnlyList<WritingSystemDisplay> WritingSystems)
{
    public BaselineToken? Baseline { get; init; }
}
