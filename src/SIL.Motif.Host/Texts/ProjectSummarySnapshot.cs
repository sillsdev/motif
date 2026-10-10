using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Texts;

/// <summary>Counts and exact word identities captured from one saved FieldWorks project.</summary>
public sealed record ProjectSummarySnapshot(
    int WordCount,
    int OccurrenceCount,
    int WordformCount,
    int RuleCount,
    int LexemeCount,
    IReadOnlyList<string> Wordforms,
    IReadOnlyList<ProjectTextSummary> Texts)
{
    [System.Text.Json.Serialization.JsonRequired]
    public IReadOnlyDictionary<string, string?> WordWritingSystems { get; init; } = new Dictionary<string, string?>();

    [System.Text.Json.Serialization.JsonRequired]
    public IReadOnlyList<WritingSystemDisplay> WritingSystems { get; init; } = [];

    /// <summary>An empty summary for synthetic Baselines that have no readable project model.</summary>
    public static ProjectSummarySnapshot Empty { get; } = new(0, 0, 0, 0, 0, [], []);
}

/// <summary>Word occurrences for one Text, keyed by the Text's exact FieldWorks identity.</summary>
public sealed record ProjectTextSummary(
    Guid TextId,
    string Title,
    int WordCount,
    int OccurrenceCount,
    IReadOnlyDictionary<string, int> OccurrencesByWord)
{
    /// <summary>The distinct spellings with an analysed occurrence in this captured Text.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public int InterlinearizedWordCount { get; init; }

    /// <summary>The analysed occurrences in this captured Text, counted by spelling.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public int InterlinearizedOccurrenceCount { get; init; }
}
