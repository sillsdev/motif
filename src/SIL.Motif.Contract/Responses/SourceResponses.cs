using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The wire version of the Source responses, raised when their shape changes.</summary>
public static class SourceWire
{
    public const int SchemaVersion = 1;
}

/// <summary>The chapters of one book in a Source's Context.</summary>
/// <param name="Book">The three-letter book id.</param>
/// <param name="Chapters"><c>all</c> or the chapter text, such as <c>13-15</c>.</param>
public sealed record SourceContextRange(string Book, string Chapters);

/// <summary>One Source as the list shows it. Wire values are strings, so this record carries no Scripture types.</summary>
/// <param name="Id">The Source id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind"><c>Paratext</c> or <c>Corpus</c>.</param>
/// <param name="Status"><c>Active</c> or <c>Removed</c>.</param>
/// <param name="Versification">The versification name as stored; empty when the item has none.</param>
/// <param name="Context">The Context text as stored.</param>
/// <param name="ContextRanges">The parsed Context; empty when <paramref name="Error"/> is set.</param>
/// <param name="Folder">The folder of the Source on this machine, or null when none is recorded.</param>
/// <param name="Error">Why the item cannot be used, or null.</param>
/// <param name="Place">Where the asked chapter sits (<c>InContext</c>, <c>Current</c>, <c>Later</c>); null when no chapter was asked or it cannot be told.</param>
public sealed record SourceSummary(
    string Id, string Name, string Kind, string Status, string Versification, string Context,
    IReadOnlyList<SourceContextRange> ContextRanges, string? Folder, string? Error, string? Place = null);

/// <summary>The Sources of the current Baseline.</summary>
public sealed record SourceListResponse(IReadOnlyList<SourceSummary> Sources)
{
    public int SchemaVersion { get; init; } = SourceWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    /// <summary>Whether the live file has been saved since this Baseline.</summary>
    public bool IsStale { get; init; }
}

/// <summary>One Source with its chapter count.</summary>
/// <param name="Source">The Source.</param>
/// <param name="ChapterCount">How many chapters its Context selects; 0 when the Context is unusable.</param>
public sealed record SourceShowResponse(SourceSummary Source, int ChapterCount)
{
    public int SchemaVersion { get; init; } = SourceWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    public bool IsStale { get; init; }
}

/// <summary>Where the chapter sits in one Source.</summary>
/// <param name="Source">The Source id.</param>
/// <param name="Place"><c>InContext</c>, <c>Current</c> or <c>Later</c>; null when it cannot be told.</param>
/// <param name="Error">Why it cannot be told, or null.</param>
public sealed record SourcePlace(string Source, string? Place, string? Error);

/// <summary>Where a chapter sits in each asked Source.</summary>
/// <param name="Ref">The chapter, normalized to <c>MAT 5</c>.</param>
/// <param name="Places">One row per Source.</param>
public sealed record SourceWhereResponse(string Ref, IReadOnlyList<SourcePlace> Places)
{
    public int SchemaVersion { get; init; } = SourceWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    public bool IsStale { get; init; }
}

/// <summary>A Source's Current chapter.</summary>
/// <param name="Source">The Source id.</param>
/// <param name="Chapter">The chapter, such as <c>MAT 5</c>.</param>
/// <param name="Place">Where it sits against the Context (<c>InContext</c> or <c>Current</c>); null when the Source or its text is unavailable.</param>
public sealed record CurrentChapter(string Source, string Chapter, string? Place);

/// <summary>The Current chapters recorded on this machine; empty is fine.</summary>
public sealed record CurrentGetResponse(IReadOnlyList<CurrentChapter> Current)
{
    public int SchemaVersion { get; init; } = SourceWire.SchemaVersion;
    public BaselineToken? Baseline { get; init; }
    public bool IsStale { get; init; }
}
