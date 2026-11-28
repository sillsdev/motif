using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The wire version of the Grammar Guide section responses, raised when their shape changes.</summary>
public static class GdrWire
{
    public const int SchemaVersion = 6;
}

/// <summary>One Grammar Guide section as the list shows it.</summary>
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

/// <summary>The current Baseline's Grammar Guide, without opening the FieldWorks project.</summary>
public sealed record GdrListResponse(bool HasBaseline, IReadOnlyList<GdrSummary> Records)
{
    public int SchemaVersion { get; init; } = GdrWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    /// <summary>Whether the live file has been saved since this Baseline; its records remain readable.</summary>
    public bool IsStale { get; init; }
    /// <summary>Whether the list is still named "Grammar Design Records"; it is read, but should be renamed "Grammar Guide".</summary>
    public bool UsesOldListName { get; init; }
}

/// <summary>One Grammar Guide section with its Discussion as paragraphs of runs, and what they need to be drawn.</summary>
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
    /// <summary>The title's stored alternatives, including their writing systems.</summary>
    public IReadOnlyList<RichStringAlternative> TitleAlternatives { get; init; } = [];
    /// <summary>The abstract's stored alternatives, with their rich runs.</summary>
    public IReadOnlyList<RichStringAlternative> DescriptionAlternatives { get; init; } = [];
    /// <summary>The record's position in the list's depth-first order.</summary>
    public int Position { get; init; }
}

/// <summary>What staging a Grammar Guide edit added to a Draft.</summary>
/// <param name="DraftName">The Draft the edit is staged in.</param>
/// <param name="Id">The section's canonical id.</param>
/// <param name="Abbreviation">The section's <c>GDR-n</c> number.</param>
/// <param name="Fields">The fields the edit changed, such as discussion or title (ws); empty when nothing differed.</param>
/// <param name="OperationCount">The Draft operations staged for the section.</param>
/// <param name="Warnings">Findings from reading the Markdown that do not stop the edit.</param>
/// <remarks><see cref="StylesCreated"/> lists the FieldWorks standard styles the Draft also creates because the project lacks them.</remarks>
public sealed record GdrEditResponse(
    string DraftName, string Id, string Abbreviation, IReadOnlyList<string> Fields, int OperationCount, IReadOnlyList<string> Warnings)
{
    /// <summary>The standard styles the Draft also creates; empty when the project had them all.</summary>
    public IReadOnlyList<string> StylesCreated { get; init; } = [];
}

/// <summary>One decision of a batch as it is or would be written.</summary>
/// <param name="Row">The line of the file it came from.</param>
/// <param name="Abbreviation">The section's permanent <c>GDR-n</c> number.</param>
/// <param name="Entry">The list item written under the section's Decisions heading.</param>
public sealed record GdrDecidedRow(int Row, string Abbreviation, string Entry);

/// <summary>What a batch of judgement calls staged, or, on a dry run, would stage.</summary>
/// <param name="DraftName">The Draft they are staged in, or would be.</param>
/// <param name="Staged">False on a dry run, which stages nothing.</param>
/// <param name="Rows">Each row's result, in file order.</param>
/// <param name="OperationCount">The Draft operations staged; zero on a dry run.</param>
/// <param name="StylesCreated">The standard styles the Draft also creates; empty when the project had them all.</param>
public sealed record GdrDecideBatchResponse(
    string DraftName, bool Staged, IReadOnlyList<GdrDecidedRow> Rows, int OperationCount, IReadOnlyList<string> StylesCreated);

/// <summary>What staging a new Grammar Guide item added to a Draft.</summary>
/// <param name="DraftName">The Draft the item is staged in.</param>
/// <param name="Id">The new item's canonical id.</param>
/// <param name="Abbreviation">The permanent <c>GDR-n</c> number it was given.</param>
/// <param name="ParentId">The canonical id of the item it goes under, or null at the top of the list.</param>
/// <param name="ListCreated">Whether the project had no Grammar Guide list, so the Draft also creates it.</param>
/// <param name="Fields">The fields staged beyond the item itself, such as discussion.</param>
/// <param name="OperationCount">The Draft operations staged for the item.</param>
/// <param name="Warnings">Findings from reading the Markdown that do not stop the add.</param>
/// <remarks><see cref="StylesCreated"/> lists the FieldWorks standard styles the Draft also creates because the project lacks them.</remarks>
public sealed record GdrAddResponse(
    string DraftName, string Id, string Abbreviation, string? ParentId, bool ListCreated, IReadOnlyList<string> Fields,
    int OperationCount, IReadOnlyList<string> Warnings)
{
    /// <summary>The standard styles the Draft also creates; empty when the project had them all.</summary>
    public IReadOnlyList<string> StylesCreated { get; init; } = [];
}

/// <summary>What refreshing one query block's saved table did.</summary>
/// <param name="QueryId">The block's <c>id</c>.</param>
/// <param name="Outcome"><c>saved</c> (staged), <c>unchanged</c> (the saved table already matches) or <c>skipped</c> (see <paramref name="Message"/>).</param>
/// <param name="Message">Why a block was skipped, or the headline of what was saved.</param>
/// <param name="RowsSaved">The rows in the table now; at most 10.</param>
/// <param name="RowsInAll">The rows the query has in all; 0 when skipped.</param>
public sealed record GdrRefreshedTable(string QueryId, string Outcome, string Message, int RowsSaved, long RowsInAll);

/// <summary>One section's blocks as refreshed.</summary>
public sealed record GdrRefreshedSection(string Id, string Abbreviation, IReadOnlyList<GdrRefreshedTable> Tables);

/// <summary>What <c>guide refresh-tables</c> staged.</summary>
/// <param name="DraftName">The Draft the tables are staged in; null when nothing was staged.</param>
/// <param name="Sections">Each section with a query block, with its blocks.</param>
/// <param name="OperationCount">The Draft operations staged.</param>
/// <param name="Warnings">Findings that do not stop the refresh.</param>
public sealed record GdrRefreshTablesResponse(
    string? DraftName, IReadOnlyList<GdrRefreshedSection> Sections, int OperationCount, IReadOnlyList<string> Warnings)
{
    /// <summary>The FieldWorks standard styles the Draft also creates because the project lacks them.</summary>
    public IReadOnlyList<string> StylesCreated { get; init; } = [];
}
