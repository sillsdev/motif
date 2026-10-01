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
    IReadOnlyList<TraceRuleReading> RulesOnBestPath)
{
    /// <summary>
    /// Every morph, rule, template and stratum the reading names, once each, in the order first met. Each step,
    /// morph, stop group and rule names its entry here by <c>RefId</c>, so a link to FieldWorks or a join to Timing
    /// goes through identity rather than through display text.
    /// </summary>
    public IReadOnlyList<TraceRef> Refs { get; init; } = [];
}

/// <summary>
/// One name a trace reading shows: what kind of thing it is, the identity PanGloss gave it and how far that
/// identity can be trusted, where it opens in FieldWorks, and the key Timing records it under.
/// </summary>
/// <param name="Id">The reading-wide id other records cite as <c>RefId</c>, built from the identity when there is one.</param>
/// <param name="Kind">
/// <c>morph</c>, <c>affixRule</c>, <c>compoundRule</c>, <c>phonologicalRule</c>, <c>template</c> or <c>stratum</c>.
/// </param>
/// <param name="Label">The name the project gives it, as the reading first shows it.</param>
public sealed record TraceRef(string Id, string Kind, string Label)
{
    /// <summary>A morph's gloss; <see langword="null"/> for a rule, template or stratum.</summary>
    public string? Gloss { get; init; }

    /// <summary>
    /// The identity the parser recorded, a FieldWorks GUID when <see cref="IdentityQuality"/> is <c>authored</c>;
    /// <see langword="null"/> when the trace recorded none, as in a v1 document.
    /// </summary>
    public string? Identity { get; init; }

    /// <summary>
    /// <c>authored</c> for an identity taken from the FieldWorks project, <c>grammar-local</c> for one that holds only
    /// within this grammar, <c>synthetic</c> for one the parser made up, and <c>unknown</c> when none was recorded.
    /// </summary>
    public string IdentityQuality { get; init; } = TraceRefIds.UnknownQuality;

    /// <summary>
    /// Where this opens in FieldWorks; <see langword="null"/> unless the identity is authored, the project still
    /// holds the object, and the trace's Baseline belongs to the project being read.
    /// </summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }

    /// <summary>
    /// The object kind and key PanGloss's statistics record this under; <see langword="null"/> for a template, a
    /// stratum, or a name with no identity.
    /// </summary>
    public TraceTimingKey? TimingKey { get; init; }
}

/// <summary>A FieldWorks destination: the tool, the name FieldWorks shows for it, the object, and the link itself.</summary>
public sealed record TraceFieldWorksTarget(string Tool, string ToolName, string ObjectId, string Link);

/// <summary>An object's identity in PanGloss's statistics: its kind there, such as <c>phon_rule</c>, and its key.</summary>
public sealed record TraceTimingKey(string Kind, string Key);

/// <summary>Builds the reading-wide id a step or morph cites as <c>RefId</c>, from identity first and label last.</summary>
public static class TraceRefIds
{
    /// <summary>The identity quality of a name whose trace recorded no identity.</summary>
    public const string UnknownQuality = "unknown";

    /// <summary>
    /// The id of the rule, template or stratum a step names: its recorded identity kind and id, or, when the trace
    /// recorded none, the kind its step type implies and its label; <see langword="null"/> for a step naming nothing.
    /// </summary>
    public static string? ForSource(string type, string? source, string? identityKind, string? identityId)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!string.IsNullOrEmpty(identityKind) && !string.IsNullOrEmpty(identityId))
            return identityKind + ":" + identityId;
        if (string.IsNullOrWhiteSpace(source)) return null;
        return IdentityKindOf(type) is { } kind ? kind + ":name:" + source : null;
    }

    /// <summary>
    /// The id of a morph: its entry, grammatical info and allomorph GUIDs; else the grammar's own morpheme and
    /// allomorph numbers; else the guessed or written form; <see langword="null"/> for a morph with none of them.
    /// </summary>
    public static string? ForMorph(TraceMorph morph)
    {
        ArgumentNullException.ThrowIfNull(morph);
        if (morph.EntryId is not null || morph.MsaId is not null || morph.FormId is not null)
            return $"morph:{morph.EntryId}/{morph.MsaId}/{morph.FormId}";
        if (morph.MorphemeId is { } morpheme) return $"morph:#{morpheme}.{morph.AllomorphId}";
        if (!string.IsNullOrEmpty(morph.GuessedString)) return "morph:guess:" + morph.GuessedString;
        return string.IsNullOrEmpty(morph.Form) ? null : "morph:name:" + morph.Form;
    }

    /// <summary>The identity kind PanGloss records for a step of <paramref name="type"/>, or <see langword="null"/>.</summary>
    public static string? IdentityKindOf(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Contains("PhonologicalRule", StringComparison.Ordinal)) return "phonRule";
        if (type.Contains("MorphologicalRule", StringComparison.Ordinal) ||
            type.Contains("CompoundingRule", StringComparison.Ordinal)) return "morphRule";
        if (type.Contains("Template", StringComparison.Ordinal)) return "template";
        return type.Contains("Stratum", StringComparison.Ordinal) || type == "LexicalLookup" ? "stratum" : null;
    }
}

/// <summary>Attempts stopped by one rule identity and reason, ordered by how many stopped.</summary>
public sealed record TraceStopGroup(
    string? Rule, string? RuleId, string? ReasonCode, string? Explanation,
    IReadOnlyList<TraceCandidate> Attempts)
{
    public int Count => Attempts.Count;

    /// <summary>The <see cref="TraceRef.Id"/> of the rule that stopped these attempts, if a rule did.</summary>
    public string? RuleRefId { get; init; }
}

/// <summary>A named rule on the best attempt, with its building-order reading and original step ids.</summary>
public sealed record TraceRuleReading(
    string Rule, string? RuleId, string Kind, string Outcome, string Explanation,
    IReadOnlyList<string> StepIds)
{
    /// <summary>The <see cref="TraceRef.Id"/> of this rule.</summary>
    public string? RefId { get; init; }
}

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

    /// <summary>The <see cref="TraceRef.Id"/> this morph names, as <see cref="TraceRefIds.ForMorph"/> builds it.</summary>
    public string? RefId => TraceRefIds.ForMorph(this);
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

    /// <summary>The <see cref="TraceRef.Id"/> of <see cref="StoppedByRule"/>.</summary>
    public string? StoppedByRefId { get; init; }
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

    /// <summary>The <see cref="TraceRef.Id"/> of the rule, template or stratum this step names, if any.</summary>
    public string? RefId => TraceRefIds.ForSource(Type, Source, SourceIdentityKind, SourceIdentityId);
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
