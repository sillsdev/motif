using System;
using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>The recorded attempts and their shared reading, independent of a front end.</summary>
public sealed record WordTraceReading(
    string Word,
    TraceStep Root,
    IReadOnlyList<TraceCandidate> Attempts,
    IReadOnlyList<TraceAnalysis> Analyses,
    IReadOnlyList<TraceStopGroup> StopGroups,
    IReadOnlyList<TraceCandidate> ClosestAttempts,
    IReadOnlyList<TraceRuleReading> RulesOnBestPath);

/// <summary>Attempts stopped by one rule identity and reason, ordered by how many stopped.</summary>
public sealed record TraceStopGroup(
    string? Rule, string? RuleId, string? ReasonCode, string? Explanation,
    IReadOnlyList<TraceCandidate> Attempts)
{
    public int Count => Attempts.Count;
}

/// <summary>A named rule on the best attempt, with its building-order reading and original step ids.</summary>
public sealed record TraceRuleReading(
    string Rule, string? RuleId, string Kind, string Outcome, string Explanation,
    IReadOnlyList<string> StepIds);

/// <summary>The parser response plus the complete portable diagnostic envelope.</summary>
public sealed record WordTraceResponse(
    string Word,
    bool Parsed,
    bool Complete,
    string? StopReason,
    int StepCount,
    string? DeepestRule,
    int ElapsedMs,
    IReadOnlyList<TraceCandidate> Candidates,
    TraceStep Root)
{
    public WordTraceReading? Reading { get; init; }
    public long? ParserSteps { get; init; }
    public double? ParserElapsedMs { get; init; }
    public bool Guessed { get; init; }
    public IReadOnlyList<TraceEffort> Effort { get; init; } = [];
    public string DiagnosticJson { get; init; } = string.Empty;
    public string DiagnosticFormat { get; init; } = string.Empty;
    public string SearchStatus { get; init; } = "complete";
    public bool InvalidShape { get; init; }
    public IReadOnlyList<TraceAnalysis> Analyses { get; init; } = [];
    public TraceHostCapture? HostCapture { get; init; }
    public TraceProvenanceComparison? Provenance { get; init; }
    public string? ParserName { get; init; }
    public string? ParserVersion { get; init; }
    public string? TraceProfile { get; init; }
    public string? GrammarHash { get; init; }
    public string? GrammarHashSemantics { get; init; }
}

public sealed record TraceEffort(
    string Kind, long Attempts, long Outputs, long NotApplied, long NoRoot, long SurfaceMismatch, long Uses, double? SelfMs)
{
    public long Work { get; init; }
    public bool TimingAvailable => SelfMs is not null;
}

public sealed record TraceAnalysis(
    string? AnalysisId,
    int? Index,
    string? Surface,
    string Availability,
    IReadOnlyList<TraceMorph> Morphs)
{
    public string? Signature { get; init; }
    public int FoundWays { get; init; } = 1;
    public IReadOnlyList<string> ProducerAnalysisIds { get; init; } = [];
    public string? LegacyMorphemes { get; init; }
    public string? ProjectionStatus { get; init; }
    public string? ProjectionError { get; init; }
}

public sealed record TraceMorph(
    string? Identity,
    string? Form,
    string? Headword,
    string? Gloss,
    string? Category,
    string? Slot,
    string? InflectionClass,
    string? Features,
    string? GuessedString,
    string? FieldWorksLink)
{
    public string? FormId { get; init; }
    public string? EntryId { get; init; }
    public string? MsaId { get; init; }
    public string? InflTypeId { get; init; }
    public string? IdentityQuality { get; init; }
    public int? MorphemeId { get; init; }
    public int? AllomorphId { get; init; }
    public IReadOnlyList<string?> SourceFormIds { get; init; } = [];
    public string? FormSourceId { get; init; }
    public string? HeadwordSourceId { get; init; }
    public string? GlossSourceId { get; init; }
    public string? FormWritingSystem { get; init; }
    public string? HeadwordWritingSystem { get; init; }
    public string? GlossWritingSystem { get; init; }
    public string? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryAbbreviation { get; init; }
    public string? SlotId { get; init; }
    public bool? SlotOptional { get; init; }
    public string? InflectionClassId { get; init; }
    public string? InflectionClassName { get; init; }
    public string? InflectionClassAbbreviation { get; init; }
    public string? FeaturesSource { get; init; }
    public string? FeaturesStatus { get; init; }
    public string? RawJson { get; init; }
}

public sealed record TraceCandidate(
    IReadOnlyList<ParserReadingMorph> Morphs,
    bool Succeeded,
    string? FailureReason,
    string? Explanation,
    IReadOnlyList<TraceStep> Steps)
{
    public string? AttemptId { get; init; }
    public string? OutcomeStatus { get; init; }
    public string? ContextualFailure { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }
    public string MorphAvailability { get; init; } = "unavailable";
    public IReadOnlyList<TraceMorph> RichMorphs { get; init; } = [];

    /// <summary>The form this attempt had built when it ended, or <see langword="null"/> when none was recorded.</summary>
    public string? Surface { get; init; }

    /// <summary>
    /// The rule whose step failed just before this attempt ended, by the name the project gives it; <see langword="null"/>
    /// when the attempt failed on its own terms, such as leaving morphemes unused.
    /// </summary>
    public string? StoppedByRule { get; init; }

    /// <summary>The trace's own identifier for <see cref="StoppedByRule"/>, a FieldWorks GUID for an authored rule.</summary>
    public string? StoppedByRuleId { get; init; }
}
public sealed record TraceStep(
    string Type,
    string? Source,
    string? Input,
    string? Output,
    string? FailureReason,
    IReadOnlyList<TraceStep> Children)
{
    public string StepId { get; init; } = string.Empty;
    public int? Subrule { get; init; }
    public string? OutcomeStatus { get; init; }
    public string? OutcomeEventType { get; init; }
    public string? ContextualFailure { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public IReadOnlyList<TraceMorph> AttemptedMorphs { get; init; } = [];
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }
}

public sealed record TraceHostCapture(
    string? ProjectIdentity,
    string? GrammarHash,
    string? GrammarHashSemantics,
    string? BundleDigest,
    DateTimeOffset? CapturedUtc,
    long? WallElapsedMs,
    IReadOnlyList<TraceWritingSystem> WritingSystems);

public sealed record TraceWritingSystem(
    string Id,
    string? Name,
    bool IsVernacular,
    bool IsDefault,
    string? Direction,
    string? Font);

public sealed record TraceProvenanceComparison(
    string ProjectIdentityStatus,
    string GrammarStatus,
    string WritingSystemsStatus,
    bool IsCompatible,
    string Warning)
{
    public bool CanNavigate { get; init; }
}
