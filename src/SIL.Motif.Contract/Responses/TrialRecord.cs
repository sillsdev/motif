using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Contract.Responses;

/// <summary>How a Proposal's latest Trial relates to the project and Proposal now.</summary>
public enum TrialFreshness
{
    /// <summary>The completed Trial used the current Baseline and Proposal content.</summary>
    Current,
    /// <summary>The project published a different Baseline after the Trial.</summary>
    BaselineMoved,
    /// <summary>The Proposal content changed after the Trial.</summary>
    ProposalChanged,
    /// <summary>The Trial has not completed with a full Assessment result.</summary>
    Incomplete,
    /// <summary>No Trial exists for this Proposal.</summary>
    None,
}

/// <summary>Which words a Trial was asked to measure.</summary>
public enum TrialWordScope
{
    /// <summary>The caller named the words, or the Proposal's own touched words were chosen for it.</summary>
    ChosenWords,
    /// <summary>The words of the project's Default Selection, as the Trial resolved them.</summary>
    DefaultSelection,
    /// <summary>Every wordform in the project.</summary>
    AllWords,
}

/// <summary>One Proposal's latest Trial and the evidence it produced.</summary>
/// <param name="JobId">The durable Trial job id.</param>
/// <param name="ProposalId">The Proposal identity carried by the Trial.</param>
/// <param name="IntentDigest">The Proposal content measured by the Trial.</param>
/// <param name="Baseline">The Baseline measured by the Trial, or the current Baseline while it is queued.</param>
/// <param name="DryRun">The Trial's published Dry Run, when it has reached that step.</param>
/// <param name="AssessmentIds">The completed Trial's Assessment ids keyed by kind.</param>
/// <param name="Words">The words selected by the Trial's Assessment scope.</param>
/// <param name="AllWords">Whether the Trial explicitly selected all wordforms.</param>
/// <param name="Limits">The Trial's optional per-word limits.</param>
/// <param name="Status">The durable job status.</param>
/// <param name="CompletedUtc">The job's terminal timestamp, or <see langword="null"/> while it is active.</param>
/// <param name="Freshness">Whether the completed evidence is still current.</param>
public sealed record TrialRecord(
    string JobId,
    string ProposalId,
    string IntentDigest,
    BaselineToken Baseline,
    DryRunProjection? DryRun,
    IReadOnlyDictionary<string, string> AssessmentIds,
    IReadOnlyList<string> Words,
    bool AllWords,
    TrialLimits? Limits,
    JobStatus Status,
    DateTimeOffset? CompletedUtc,
    TrialFreshness Freshness)
{
    /// <summary>The Proposal label at read time, or <see langword="null"/> when it has no label.</summary>
    public string? Label { get; init; }

    /// <summary>The named grammar check for this Trial revision, when the Trial produced one.</summary>
    public string? GrammarCheckId { get; init; }

    /// <summary>The root ParseTime word results when the caller requested them.</summary>
    public IReadOnlyList<AssessmentWordResult> WordResults { get; init; } = [];

    /// <summary>
    /// For a Proposal that removes an exception feature: whether the Trial words kept the same readings before and after
    /// (ADR 0118, built on ADR 0115). Null for any other Proposal and for a Trial that has not finished.
    /// </summary>
    public ReadingIdentityResult? ReadingIdentity { get; init; }

    /// <summary>Parser work compared with the Baseline on the Trial words, when available.</summary>
    public TrialWork? Work { get; init; }

    /// <summary>A grammar-check failure that did not prevent the Trial from continuing.</summary>
    public string? GrammarCheckWarning { get; init; }

    /// <summary>
    /// The grammar PanGloss dropped with only a warning while checking the candidate, split into what the
    /// Proposal adds or edits and what the Baseline already had; <see langword="null"/> when the Trial recorded none.
    /// </summary>
    public DroppedGrammar? DroppedGrammar { get; init; }

    /// <summary>
    /// True only when the Trial checked for dropped grammar and the Proposal introduced none; false when it
    /// introduced some, and false when the check did not run or the Trial has not finished. It says nothing
    /// about the correctness numbers, which a Trial reports separately.
    /// </summary>
    public bool Clean => DroppedGrammar is { IsClean: true };

    /// <summary>The recorded check, or a not-checked result when the Trial recorded none.</summary>
    [JsonIgnore]
    public DroppedGrammar DroppedGrammarOrUnchecked =>
        DroppedGrammar ?? Responses.DroppedGrammar.NotChecked("The Trial recorded no dropped-grammar check.");

    /// <summary>Which words the Trial was asked to measure.</summary>
    /// <remarks>Unset, it follows <see cref="AllWords"/>: all words, or words someone chose.</remarks>
    public TrialWordScope WordScope
    {
        get => _wordScope ?? (AllWords ? TrialWordScope.AllWords : TrialWordScope.ChosenWords);
        init => _wordScope = value;
    }

    private readonly TrialWordScope? _wordScope;

    /// <summary>What the person can do next with this Trial's Proposal; see <see cref="TrialStanding"/>.</summary>
    public TrialStanding Standing => TrialStanding.From(Status, Freshness, DroppedGrammarNotChecked);

    /// <summary>
    /// Whether a completed Trial has no completed check for dropped grammar, which only another Trial can settle.
    /// Set when the Trial is read from the store; a record built any other way leaves it false.
    /// </summary>
    public bool DroppedGrammarNotChecked { get; init; }

    /// <summary>
    /// Whether this Trial's numbers can stand beside today's: it completed with a parse-time Assessment, whatever
    /// its word scope, because a word the Trial did not measure carries today's value into the Trial's totals
    /// (pinned by <c>OverviewForATouchedWordTrialCarriesTodaysUnmeasuredValues</c>). A stale Trial stays
    /// comparable; its <see cref="Standing"/> says it needs another try.
    /// </summary>
    [JsonIgnore]
    public bool HasComparableNumbers => Status == JobStatus.Completed && Freshness != TrialFreshness.Incomplete &&
        AssessmentIds.ContainsKey(AssessmentKinds.ParseTime);
}
