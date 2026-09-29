using System.Text.Json.Serialization;
using SIL.Motif.Contract.Requests;

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

    /// <summary>The Text occurrence that supplied context for an in-text opinion change.</summary>
    public OccurrenceAnchor? Occurrence { get; init; }

    /// <summary>The exact stored analysis selected by an in-text opinion change.</summary>
    public string? StoredAnalysisId { get; init; }

    /// <summary>The exact Assessment reading selected by an in-text change.</summary>
    public int? ReadingIndex { get; init; }
}

/// <summary>One reading shown beside a change, with its previous opinion and whether the change touches it.</summary>
public sealed record ReviewAnalysis(ParserReading Reading, string Opinion, bool Touched, bool Stored);

/// <summary>The fit of one authored change, including missing mapping or fingerprint evidence.</summary>
public sealed record ChangeFit
{
    [JsonConstructor]
    public ChangeFit(string changeId, string status, IReadOnlyList<string> reasons)
    {
        ChangeId = changeId;
        Status = status;
        Reasons = reasons;
    }

    public ChangeFit(string changeId, bool stillFits, IReadOnlyList<string> reasons)
        : this(changeId, stillFits ? ChangeFitStatus.Fits : ChangeFitStatus.NoLongerFits, reasons) { }

    public string ChangeId { get; init; }

    /// <summary>Whether the result is in the <see cref="ChangeFitStatus.Fits"/> state.</summary>
    public bool StillFits => Status == ChangeFitStatus.Fits;

    public IReadOnlyList<string> Reasons { get; init; }

    /// <summary>The machine-readable fit state: fits, uncertain, or no-longer-fits.</summary>
    public string Status { get; init; }

    /// <summary>The sentence-token context to inspect when this change is uncertain.</summary>
    public ChangeUncertainty? Uncertainty { get; init; }

    /// <summary>The Text occurrence that was selected when this change was collected, when available.</summary>
    public OccurrenceAnchor? Occurrence { get; init; }
}

/// <summary>Sentence-token context to inspect when a pending change is uncertain.</summary>
public sealed record ChangeUncertainty(string Reason, IReadOnlyList<OccurrenceWordToken> BeforeTokens,
    IReadOnlyList<OccurrenceWordToken> AfterTokens);

/// <summary>A word token in the ordered sentence context used to explain uncertainty.</summary>
public sealed record OccurrenceWordToken(int Index, string WordformId, string Form);

/// <summary>The durable pending Draft and its per-change fit against the saved project.</summary>
public sealed record PendingChangesSnapshot(
    string? DraftId, string Revision, IReadOnlyList<PendingChange> Changes,
    IReadOnlyList<ChangeFit> FitSummary)
{
    /// <summary>The word a bulk Candidate action skipped to preserve an explicit pending choice.</summary>
    public string? SkippedWord { get; init; }

    /// <summary>The earlier pending change replaced by this authoring action.</summary>
    public string? ReplacedChangeId { get; init; }

    /// <summary>The earlier pending change cancelled by returning its reading to the project state.</summary>
    public string? CancelledChangeId { get; init; }
}
