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
    /// <summary>An empty summary for synthetic Baselines that have no readable project model.</summary>
    public static ProjectSummarySnapshot Empty { get; } = new(0, 0, 0, 0, 0, [], []);
}

/// <summary>Word occurrences for one Text, keyed by the Text's exact FieldWorks identity.</summary>
public sealed record ProjectTextSummary(
    Guid TextId,
    string Title,
    int WordCount,
    int OccurrenceCount,
    IReadOnlyDictionary<string, int> OccurrencesByWord);
