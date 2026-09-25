namespace SIL.Motif.Contract.Responses;

/// <summary>One authored change and all operations lowered from it.</summary>
public sealed record PendingChange(
    string ChangeId, string WordformId, string Word, string Kind, string? AssessmentId,
    string? DisplayReading, IReadOnlyList<string> OperationIds)
{
    /// <summary>The word's readings, including the one this change addresses.</summary>
    public IReadOnlyList<ReviewAnalysis> Analyses { get; init; } = [];

    /// <summary>The page on which this change was collected.</summary>
    public string? OriginPage { get; init; }
}

/// <summary>One reading shown beside a change, with its previous opinion and whether the change touches it.</summary>
public sealed record ReviewAnalysis(ParserReading Reading, string Opinion, bool Touched, bool Stored);

/// <summary>The fit of one authored change, including missing mapping or fingerprint evidence.</summary>
public sealed record ChangeFit(string ChangeId, bool StillFits, IReadOnlyList<string> Reasons);

/// <summary>The durable pending Draft and its per-change fit against the saved project.</summary>
public sealed record PendingChangesSnapshot(
    string? DraftId, string Revision, IReadOnlyList<PendingChange> Changes,
    IReadOnlyList<ChangeFit> FitSummary)
{
    /// <summary>The word a bulk Candidate action skipped to preserve an explicit pending choice.</summary>
    public string? SkippedWord { get; init; }

    /// <summary>The earlier pending change replaced by this authoring action.</summary>
    public string? ReplacedChangeId { get; init; }
}
