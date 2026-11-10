using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The wire version of the Grammar Design Record responses, raised when their shape changes.</summary>
public static class GdrWire
{
    public const int SchemaVersion = 5;
}

/// <summary>One Grammar Design Record as the list shows it.</summary>
/// <param name="Id">The record's canonical id.</param>
/// <param name="Abbreviation">The <c>GDR-n</c> number a person says.</param>
/// <param name="Title">The record's name.</param>
/// <param name="Description">The one-paragraph abstract; empty when the record has none.</param>
/// <param name="ParentId">The canonical id of the record this is a sub-item of, or null at the top of the list.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrSummary(string Id, string Abbreviation, string Title, string Description, string? ParentId = null);

/// <summary>An integer text property: how to read the value, and the value, as FieldWorks stores them.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrIntProperty(int Variation, int Value);

/// <summary>A run's or paragraph's text properties, keyed by FieldWorks text property number.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrTextProperties(
    IReadOnlyDictionary<int, GdrIntProperty> Ints, IReadOnlyDictionary<int, string> Strings);

/// <summary>One run of a paragraph: text in one writing system with one set of properties.</summary>
/// <param name="Text">The run's text; an object run is a single U+FFFC.</param>
/// <param name="WritingSystem">The run's writing-system tag.</param>
/// <param name="Style">The character style name, or null for none.</param>
/// <param name="Properties">Every property of the run, the style name included.</param>
/// <param name="ObjData">The run's link target or embedded-object data as FieldWorks stores it, or null for plain text.</param>
/// <param name="Target">What a link run points to, or null when the run is not a link.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrRun(
    string Text, string WritingSystem, string? Style, GdrTextProperties Properties, string? ObjData = null,
    GdrLinkTarget? Target = null);

[JsonConverter(typeof(JsonStringEnumConverter<GdrLinkState>))]
public enum GdrLinkState
{
    /// <summary>The target is an object of the project.</summary>
    [JsonStringEnumMemberName("resolved")] Resolved,
    /// <summary>The target is not in the project, or the link names none.</summary>
    [JsonStringEnumMemberName("broken")] Broken,
    /// <summary>The target is outside this project; nothing was looked up.</summary>
    [JsonStringEnumMemberName("external")] External,
}

/// <summary>What a link run points to, resolved against the Baseline.</summary>
/// <param name="State">Whether the target was found.</param>
/// <param name="Kind">The kind of object in a person's words, or null when it is not an object of the project.</param>
/// <param name="Name">The target's current name, or the address for an external link; null for a broken link.</param>
/// <param name="Description">A short description of the target; empty when it has none.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrLinkTarget(GdrLinkState State, string? Kind, string? Name, string Description);

/// <summary>One paragraph of a Discussion.</summary>
/// <param name="Style">The paragraph style name, or null for none.</param>
/// <param name="RightToLeft">Whether the paragraph runs right to left.</param>
/// <param name="DefaultWritingSystem">The writing system an empty paragraph types in.</param>
/// <param name="Properties">Every property of the paragraph, the style name and direction included.</param>
/// <param name="Runs">The paragraph's runs, in order.</param>
/// <param name="Key">
/// The paragraph's canonical id, which an edit names it by (SIL.TextEditing ADR 0006); null only for a paragraph that
/// the project has not stored yet.
/// </param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrParagraph(
    string? Style, bool RightToLeft, string DefaultWritingSystem, GdrTextProperties Properties,
    IReadOnlyList<GdrRun> Runs, string? Key = null);

[JsonConverter(typeof(JsonStringEnumConverter<GdrStyleKind>))]
public enum GdrStyleKind
{
    [JsonStringEnumMemberName("paragraph")] Paragraph,
    [JsonStringEnumMemberName("character")] Character,
}

/// <summary>A style of the project or one the Discussion uses, with its resolved properties per listed writing system.</summary>
/// <param name="Name">The style's name.</param>
/// <param name="Kind">Paragraph or character.</param>
/// <param name="ByWritingSystem">The properties after inheritance and the writing system's overrides, keyed by tag.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrStyle(
    string Name, GdrStyleKind Kind, IReadOnlyDictionary<string, GdrTextProperties> ByWritingSystem);

/// <summary>A writing system of the project or one the Discussion uses.</summary>
/// <param name="Tag">The writing-system tag.</param>
/// <param name="Direction"><c>ltr</c> or <c>rtl</c>.</param>
/// <param name="DefaultFont">The writing system's default font family.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrWritingSystem(string Tag, string Direction, string DefaultFont);

/// <summary>The current Baseline's Grammar Design Records, without opening the FieldWorks project.</summary>
public sealed record GdrListResponse(bool HasBaseline, IReadOnlyList<GdrSummary> Records)
{
    public int SchemaVersion { get; init; } = GdrWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    /// <summary>Whether the live file has been saved since this Baseline; its records remain readable.</summary>
    public bool IsStale { get; init; }
}

/// <summary>One Grammar Design Record with its Discussion as paragraphs of runs, and what they need to be drawn.</summary>
public sealed record GdrShowResponse(GdrSummary Record, IReadOnlyList<GdrParagraph> Discussion)
{
    public int SchemaVersion { get; init; } = GdrWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    /// <summary>Whether the live file has been saved since this Baseline; its records remain readable.</summary>
    public bool IsStale { get; init; }
    public string DefaultVernacular { get; init; } = "";
    public string DefaultAnalysis { get; init; } = "";
    public IReadOnlyList<GdrStyle> Styles { get; init; } = [];
    public IReadOnlyList<GdrWritingSystem> WritingSystems { get; init; } = [];
}
