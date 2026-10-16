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
        : AssessmentRecordMaterial;

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
        : AssessmentRecordDetails;
    public sealed record PhonologyExamples(PhonologyAssessmentHeader Header,
        IReadOnlyList<PhonologyExampleDetail> Rows, IReadOnlyList<PhonologyDependency> Dependencies)
        : AssessmentRecordDetails;
}
