using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;

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
    /// Every morph, rule, template and stratum identity or unidentified occurrence, in the order first met. Each step,
    /// morph, stop group and rule names its entry here by <c>RefId</c>, so a link to FieldWorks or a join to Timing
    /// goes through identity rather than through display text.
    /// </summary>
    public IReadOnlyList<TraceRef> Refs { get; init; } = [];

    /// <summary>Optional compact summaries; positions address the ordered source records in Analyses.</summary>
    public IReadOnlyList<TraceLogicalAnalysis> LogicalAnalyses { get; init; } = [];
    public IReadOnlyList<string> NoParseReasons { get; init; } = [];
}

/// <summary>
/// One name a trace reading shows: what kind of thing it is, the identity PanGloss gave it and how far that
/// identity can be trusted, where it opens in FieldWorks, and the key Timing records it under.
/// </summary>
/// <param name="Id">The reading-wide id other records cite as <c>RefId</c>, built from the identity when there is one.</param>
/// <param name="Kind">
/// <c>morph</c>, <c>morphologicalRule</c>, <c>compoundRule</c>, <c>phonologicalRule</c>,
/// <c>template</c> or <c>stratum</c>. A generic morphological identity does not distinguish an affix from a compound.
/// </param>
/// <param name="Label">The producer's captured display label, as the reading first shows it.</param>
public sealed record TraceRef(string Id, string Kind, string Label)
{
    /// <summary>The Baseline's FieldWorks name captured for this identity, separate from the producer label.</summary>
    public string? CapturedFieldWorksLabel { get; init; }

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
public sealed record TraceTimingKey(string Kind, string Key)
{
    /// <summary>Authored GUID, exact structural/synthetic key, or scoped grammar-local ordinal.</summary>
    public string IdentityQuality { get; init; } = "authored";
    /// <summary>The saved grammar scope required by a grammar-local key.</summary>
    public string? Scope { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ObjectIdentity? Identity => ObjectIdentity.Create(Kind, Key, IdentityQuality, Scope);

    /// <summary>Parses the CLI's explicit kind:key address; display labels and untyped keys are invalid.</summary>
    public static TraceTimingKey? Parse(string? address)
    {
        var colon = address?.IndexOf(':') ?? -1;
        return colon <= 0 || colon == address!.Length - 1 ? null : new(address[..colon], address[(colon + 1)..]);
    }
}

/// <summary>Builds a reading-local <c>RefId</c> from recorded identity or an occurrence address, never display text.</summary>
public static class TraceRefIds
{
    /// <summary>The identity quality of a name whose trace recorded no identity.</summary>
    public const string UnknownQuality = "unknown";

    /// <summary>
    /// The id of the rule, template or stratum a step names: its recorded identity kind and id, or, when the trace
    /// recorded none, the kind its step type implies and its document-local step address.
    /// <see langword="null"/> for a step naming nothing or having no recorded identity or step address.
    /// </summary>
    public static string? ForSource(string type, string? source, string? identityKind, string? identityId, string? stepId = null,
        string? identityQuality = "authored", string? identityScope = null, string? documentScope = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!string.IsNullOrEmpty(identityKind) && !string.IsNullOrEmpty(identityId))
            return Address(ObjectIdentity.Create(identityKind, identityId, identityQuality, identityScope));
        if (string.IsNullOrWhiteSpace(source)) return null;
        return stepId is { Length: > 0 } && IdentityKindOf(type) is { } kind
            ? Address(ObjectIdentity.Create(kind, stepId, "occurrence", documentScope)) : null;
    }

    /// <summary>
    /// The id of a morph: its entry, grammatical info and allomorph GUIDs; else the grammar's own morpheme and
    /// allomorph numbers; else its document-local occurrence address. Display spelling is never identity.
    /// </summary>
    public static string? ForMorph(TraceMorph morph)
    {
        ArgumentNullException.ThrowIfNull(morph);
        var quality = morph.IdentityQuality ?? TraceRefIds.UnknownQuality;
        if (morph.EntryId is not null || morph.MsaId is not null || morph.FormId is not null)
        {
            var parts = new[] { ObjectIdentity.Create("entry", morph.EntryId, quality, morph.IdentityScope),
                ObjectIdentity.Create("msa", morph.MsaId, quality, morph.IdentityScope),
                ObjectIdentity.Create("form", morph.FormId, quality, morph.IdentityScope) };
            if (parts.All(part => part is null)) return null;
            return parts.All(part => part is null || part.Domain == "authored")
                ? $"morph:{parts[0]?.Key}/{parts[1]?.Key}/{parts[2]?.Key}"
                : "morph:" + System.Text.Json.JsonSerializer.Serialize(parts);
        }
        if (morph.MorphemeId is { } morpheme)
            return Address(ObjectIdentity.Create("morph", $"#{morpheme}.{morph.AllomorphId}", "grammar-local", morph.IdentityScope));
        return Address(ObjectIdentity.Create("morph", morph.OccurrenceId, "occurrence", morph.DocumentScope));
    }

    /// <summary>Formats a GUID identity in D format while preserving a non-GUID grammar-local key verbatim.</summary>
    public static string? CanonicalIdentity(string? identity, string? quality = "authored") =>
        ObjectIdentity.CanonicalKey(identity, quality);

    private static string? Address(ObjectIdentity? identity) => identity is null ? null
        : identity.Domain == "authored" ? identity.Kind + ":" + identity.Key
        : identity.Kind + ":" + System.Text.Json.JsonSerializer.Serialize(identity);

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

/// <summary>
/// Attempts stopped by one typed rule ref and reason, ordered by how many stopped. Unidentified stopping
/// occurrences remain separate; an attempt with no stopping ref forms its own group.
/// </summary>
public sealed record TraceStopGroup(
    string? Rule, string? RuleId, string? ReasonCode, string? Explanation,
    IReadOnlyList<TraceCandidate> Attempts)
{
    public int Count => Attempts.Count;

    /// <summary>The <see cref="TraceRef.Id"/> of the rule that stopped these attempts, if a rule did.</summary>
    public string? RuleRefId { get; init; }
}

/// <summary>One rule event on the best attempt, in building order, with its document-local step address.</summary>
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
    WordTraceReading Reading)
{
    public long? ParserSteps { get; init; }
    public double? ParserElapsedMs { get; init; }
    public bool Guessed { get; init; }
    public IReadOnlyList<TraceEffort> Effort { get; init; } = [];
    public string DiagnosticJson { get; init; } = string.Empty;
    public string DiagnosticFormat { get; init; } = string.Empty;
    public string SearchStatus { get; init; } = "complete";
    public bool InvalidShape { get; init; }

    /// <summary>The recorded search state, independently of whether an analysis was found.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TraceSearchCompletion SearchCompletion => InvalidShape ? TraceSearchCompletion.InvalidShape
        : !Complete || SearchStatus == "incomplete" ? TraceSearchCompletion.Incomplete
        : SearchStatus == "complete" ? TraceSearchCompletion.Complete : TraceSearchCompletion.NotRecorded;

    public TraceHostCapture? HostCapture { get; init; }
    public TraceProvenanceComparison? Provenance { get; init; }
    public string? ParserName { get; init; }
    public string? ParserVersion { get; init; }
    public string? TraceProfile { get; init; }
    public string? GrammarHash { get; init; }
    public string? GrammarHashSemantics { get; init; }
    public string? GrammarSource { get; init; }
    public TraceEvidenceAvailability GrammarSourceAvailability => GrammarSource is null && HostCapture?.Baseline is null
        ? TraceEvidenceAvailability.NotRecorded : TraceEvidenceAvailability.Recorded;
}

/// <summary>The search completion evidence available in a trace response.</summary>
public enum TraceSearchCompletion
{
    /// <summary>The declared search finished.</summary>
    Complete,
    /// <summary>The declared search did not finish.</summary>
    Incomplete,
    /// <summary>The word's shape prevented the search from running.</summary>
    InvalidShape,
    /// <summary>No supported search completion fact was recorded.</summary>
    NotRecorded,
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
    /// <summary>This record’s display rendering; equal renderings do not establish morphology equality.</summary>
    public string? Signature { get; init; }
    public string? LegacyMorphemes { get; init; }
    public string? ProjectionStatus { get; init; }
    public string? ProjectionError { get; init; }
    public string? ProjectionErrorCode { get; init; }
}

/// <summary>Source records with equal exact ordered authored morphology; uncertain records remain separate.</summary>
/// <param name="Signature">The representative display rendering, never an equality key.</param>
/// <param name="SourcePositions">Zero-based positions in the ordered source Analyses list.</param>
public sealed record TraceLogicalAnalysis(string Signature, IReadOnlyList<int> SourcePositions)
{
    public int RecordCount => SourcePositions.Count;
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
    /// <summary>The occurrence address within this diagnostic, used when no object identity was recorded.</summary>
    public string? OccurrenceId { get; init; }

    /// <summary>The saved grammar or diagnostic scope for grammar-local ordinals.</summary>
    public string? IdentityScope { get; init; }
    /// <summary>The unchanged saved diagnostic containing this occurrence.</summary>
    public string? DocumentScope { get; init; }

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
    public TraceEvidenceAvailability ReasonAvailability => FailureReason is { Length: > 0 }
        ? TraceEvidenceAvailability.Recorded : TraceEvidenceAvailability.NotRecorded;
    public TraceEvidenceAvailability RejectionDetailsAvailability => FailureEvidence is { Status: "available" or "recorded" or "captured" }
        || FailureRequired is not null || FailureActual is not null || FailureEnvironment is not null
        ? TraceEvidenceAvailability.Recorded : TraceEvidenceAvailability.NotRecorded;

    /// <summary>Earlier siblings recorded in the tree; membership in this derivation is not established.</summary>
    public IReadOnlyList<TraceTreeContextRange> TreeContext { get; init; } = [];

    public TraceEvidenceAvailability ExplanationAvailability => Explanation is { Length: > 0 } &&
        Explanation == FailureEvidence?.RecordedExplanation
        ? TraceEvidenceAvailability.Recorded : TraceEvidenceAvailability.NotRecorded;
    public string? AttemptId { get; init; }
    public string? OutcomeStatus { get; init; }
    public string? ContextualFailure { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public TraceFailureEvidence? FailureEvidence { get; init; }
    public TraceEventEvidence? EventEvidence { get; init; }
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }
    public string MorphAvailability { get; init; } = "unavailable";
    public IReadOnlyList<TraceMorph> RichMorphs { get; init; } = [];

    /// <summary>The form this attempt had built when it ended, or <see langword="null"/> when none was recorded.</summary>
    public string? Surface { get; init; }

    /// <summary>
    /// The rule explicitly linked by the producer to this terminal outcome, by its producer label; <see langword="null"/>
    /// when no producer link was recorded.
    /// Its captured FieldWorks name, when present, belongs to the ref identified by <see cref="StoppedByRefId"/>.
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
    public TraceEvidenceAvailability ReasonAvailability => FailureReason is { Length: > 0 }
        ? TraceEvidenceAvailability.Recorded : TraceEvidenceAvailability.NotRecorded;
    public TraceEvidenceAvailability RejectionDetailsAvailability => FailureEvidence is { Status: "available" or "recorded" or "captured" }
        || FailureRequired is not null || FailureActual is not null || FailureEnvironment is not null
        ? TraceEvidenceAvailability.Recorded : TraceEvidenceAvailability.NotRecorded;

    public string? ReasonExplanation { get; init; }
    public TraceEvidenceAvailability ExplanationAvailability => ReasonExplanation is { Length: > 0 } &&
        ReasonExplanation == FailureEvidence?.RecordedExplanation
        ? TraceEvidenceAvailability.Recorded : TraceEvidenceAvailability.NotRecorded;
    /// <summary>Motif's child-index address, stable only inside one unchanged saved tree, never across traces.</summary>
    public string StepId { get; init; } = string.Empty;
    /// <summary>The saved grammar or diagnostic scope for local object ordinals.</summary>
    public string? IdentityScope { get; init; }
    /// <summary>The unchanged saved diagnostic containing this event.</summary>
    public string? DocumentScope { get; init; }
    public int? Subrule { get; init; }
    public string? OutcomeStatus { get; init; }
    public string? OutcomeEventType { get; init; }
    public string? ContextualFailure { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public TraceFailureEvidence? FailureEvidence { get; init; }
    public TraceEventEvidence? EventEvidence { get; init; }
    public IReadOnlyList<TraceMorph> AttemptedMorphs { get; init; } = [];
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }

    /// <summary>The <see cref="TraceRef.Id"/> of the rule, template or stratum this step names, if any.</summary>
    public string? RefId => TraceRefIds.ForSource(Type, Source, SourceIdentityKind, SourceIdentityId, StepId,
        SourceIdentityQuality ?? TraceRefIds.UnknownQuality, IdentityScope, DocumentScope);
}

/// <summary>Recorded failure-owner evidence; structured operands remain diagnostic JSON, never authored notation.</summary>
public sealed record TraceFailureEvidence(
    string? Kind, string? Source, string? ReasonCode, string? Status, string? UnavailableReason,
    string? Reason, string? Required, string? Actual, string? Environment)
{
    /// <summary>The owner's explanation only when its evidence is explicitly available and includes that text.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RecordedExplanation => Status is "available" or "recorded" or "captured" && Reason is { Length: > 0 }
        ? Reason : null;

    /// <summary>Typed owner operands, retained without converting grammar-local IDs into authored notation.</summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>A captured FieldWorks name keyed by a ref's exact identity within this diagnostic.</summary>
public sealed record TraceCapturedLabel(string RefId, string Label);

public sealed record TraceHostCapture(
    string? ProjectIdentity,
    string? GrammarHash,
    string? GrammarHashSemantics,
    string? BundleDigest,
    DateTimeOffset? CapturedUtc,
    long? WallElapsedMs,
    IReadOnlyList<TraceWritingSystem> WritingSystems)
{
    /// <summary>Baseline names keyed by trace ref id, replayed without authorizing live navigation.</summary>
    public IReadOnlyList<TraceCapturedLabel> TraceLabels { get; init; } = [];

    /// <summary>The exact selected Baseline; diagnostic capture time remains CapturedUtc.</summary>
    public TraceBaselineSource? Baseline { get; init; }
}

public sealed record TraceWritingSystem(
    string Id,
    string? Name,
    bool IsVernacular,
    bool IsDefault,
    string? Direction,
    string? Font)
{
    public string? FontFeatures { get; init; }
    public IReadOnlyDictionary<string, WritingSystemStyleFont>? StyleFonts { get; init; }
    public IReadOnlyDictionary<string, double>? StyleSizes { get; init; }
}

public sealed record TraceProvenanceComparison(
    string ProjectIdentityStatus,
    string GrammarStatus,
    string WritingSystemsStatus,
    bool IsCompatible,
    string Warning)
{
    public bool CanNavigate { get; init; }
}

/// <summary>Availability of a recorded fact, independent of its display wording.</summary>
public enum TraceEvidenceAvailability
{
    Recorded,
    NotRecorded,
}

/// <summary>The saved state selected for tracing and for capture-time FieldWorks names.</summary>
public sealed record TraceBaselineSource(
    BaselineToken Token, DateTimeOffset SourceLastWriteUtc, DateTimeOffset PublishedUtc, string CaptureDescription);

/// <summary>A prefix of one parent's children in the authoritative tree, without a membership claim.</summary>
public sealed record TraceTreeContextRange(string ParentStepId, int BeforeChildIndex)
{
    /// <summary>Resolves the preceding sibling roots without copying their descendants.</summary>
    public static IEnumerable<TraceStep> Resolve(TraceStep root, IReadOnlyList<TraceTreeContextRange> ranges)
    {
        foreach (var range in ranges)
        {
            var parent = root;
            var parts = range.ParentStepId.Split('.');
            for (var index = 1; index < parts.Length; index++)
                parent = parent.Children[int.Parse(parts[index], System.Globalization.CultureInfo.InvariantCulture)];
            for (var index = 0; index < range.BeforeChildIndex; index++) yield return parent.Children[index];
        }
    }
}
