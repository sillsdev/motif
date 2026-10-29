using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Worker.Store;

/// <summary>Only a word measurement can carry word cache, timing or batch invocation material.</summary>
public abstract record AssessmentRecordMaterial
{
    private AssessmentRecordMaterial() { }

    public sealed record Words(IReadOnlyList<AssessedWord> Rows, string? CachePath, string? CacheDigest,
        IReadOnlyList<AssessmentObjectTiming> ObjectTimings, BatchInvocationEvidence? Invocation = null)
        : AssessmentRecordMaterial
    {
        /// <summary>Exact totals and the availability of object timing detail.</summary>
        public AssessmentTimingEvidence? TimingEvidence { get; init; }
    }

    public sealed record PhonologyExamples(PhonologyBaselineSource Source, PhonologyLimits RequestedLimits,
        PhonologyAssessmentOutcome Outcome) : AssessmentRecordMaterial;
}

/// <summary>A header is explicitly unloaded; loaded detail has exactly its subject's shape.</summary>
public abstract record AssessmentRecordDetails
{
    private AssessmentRecordDetails() { }

    /// <summary>A header read: no rows, but the word cache and invocation metadata the header query already joins.</summary>
    public sealed record Unloaded(string? CachePath = null, string? CacheDigest = null,
        BatchInvocationEvidence? Invocation = null) : AssessmentRecordDetails;
    public sealed record Words(IReadOnlyList<AssessedWord> Rows, string? CachePath, string? CacheDigest,
        IReadOnlyList<AssessmentObjectTiming> ObjectTimings, BatchInvocationEvidence? Invocation = null)
        : AssessmentRecordDetails
    {
        /// <summary>Exact totals and the availability of object timing detail.</summary>
        public AssessmentTimingEvidence? TimingEvidence { get; init; }
    }
    public sealed record PhonologyExamples(PhonologyAssessmentHeader Header,
        IReadOnlyList<PhonologyExampleDetail> Rows, IReadOnlyList<PhonologyDependency> Dependencies)
        : AssessmentRecordDetails;
}

/// <summary>Object totals and the completeness of the saved per-word detail.</summary>
public sealed record AssessmentTimingEvidence(
    IReadOnlyList<PanGlossObjectTimingAggregate> Aggregates,
    int DetailWordCount, bool DetailComplete, long TotalObjectTimingRows)
{
    /// <summary>Whether some words were retried after these object totals were measured.</summary>
    public bool HasRetriedWords { get; init; }
    /// <summary>Whether the run supplied no object statistics cache.</summary>
    public bool StatisticsUnavailable { get; init; }
    /// <summary>Total object time for all measured words, when any timer was recorded.</summary>
    public long? AttributedNs { get; init; }
    /// <summary>Sum of each word's nonnegative residual outside object timers.</summary>
    public long? UnattributedNs { get; init; }
    /// <summary>Sum of object time exceeding each word's total.</summary>
    public long? OverrunNs { get; init; }
}
