using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

[JsonConverter(typeof(StrictStringEnumConverter<ReviewFlagId>))]
public enum ReviewFlagId
{
    [JsonStringEnumMemberName("new-word-failures")] NewWordFailures,
    [JsonStringEnumMemberName("confirmed-readings-lost")] ConfirmedReadingsLost,
    [JsonStringEnumMemberName("known-nonwords-now-accepted")] KnownNonwordsNowAccepted,
    [JsonStringEnumMemberName("parser-work-increase")] ParserWorkIncrease,
    [JsonStringEnumMemberName("new-warnings-or-parsimony")] NewWarningsOrParsimony,
}

[JsonConverter(typeof(StrictStringEnumConverter<EvidenceState>))]
public enum EvidenceState
{
    [JsonStringEnumMemberName("complete")] Complete,
    [JsonStringEnumMemberName("partial")] Partial,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
    [JsonStringEnumMemberName("not-configured")] NotConfigured,
}

[JsonConverter(typeof(StrictStringEnumConverter<WorkComparison>))]
public enum WorkComparison
{
    [JsonStringEnumMemberName("comparable")] Comparable,
    [JsonStringEnumMemberName("not-comparable")] NotComparable,
}

[JsonConverter(typeof(StrictStringEnumConverter<DetailLevel>))]
public enum DetailLevel
{
    [JsonStringEnumMemberName("summary")] Summary,
    [JsonStringEnumMemberName("failures")] Failures,
    [JsonStringEnumMemberName("full")] Full,
}

[JsonConverter(typeof(StrictStringEnumConverter<FreshnessRelation>))]
public enum FreshnessRelation
{
    [JsonStringEnumMemberName("current")] Current,
    [JsonStringEnumMemberName("historical")] Historical,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(StrictStringEnumConverter<DetailAvailability>))]
public enum DetailAvailability
{
    [JsonStringEnumMemberName("available")] Available,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
    [JsonStringEnumMemberName("partial")] Partial,
}

[JsonConverter(typeof(StrictStringEnumConverter<Side>))]
public enum Side
{
    [JsonStringEnumMemberName("before")] Before,
    [JsonStringEnumMemberName("after")] After,
}

[JsonConverter(typeof(StrictStringEnumConverter<CoveragePrecision>))]
public enum CoveragePrecision
{
    [JsonStringEnumMemberName("exact")] Exact,
    [JsonStringEnumMemberName("lower-bound")] LowerBound,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
}

[JsonConverter(typeof(StrictStringEnumConverter<ListAvailability>))]
public enum ListAvailability
{
    [JsonStringEnumMemberName("complete")] Complete,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
    [JsonStringEnumMemberName("partial")] Partial,
}

[JsonConverter(typeof(StrictStringEnumConverter<Completion>))]
public enum Completion
{
    [JsonStringEnumMemberName("complete")] Complete,
    [JsonStringEnumMemberName("stopped")] Stopped,
    [JsonStringEnumMemberName("not-measured")] NotMeasured,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
}

[JsonConverter(typeof(StrictStringEnumConverter<Attribution>))]
public enum Attribution
{
    [JsonStringEnumMemberName("exact")] Exact,
    [JsonStringEnumMemberName("candidate")] Candidate,
    [JsonStringEnumMemberName("not-attributed")] NotAttributed,
}

[JsonConverter(typeof(StrictStringEnumConverter<CellOutcome>))]
public enum CellOutcome
{
    [JsonStringEnumMemberName("covered")] Covered,
    [JsonStringEnumMemberName("uncovered")] Uncovered,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(StrictStringEnumConverter<DiagnosticChange>))]
public enum DiagnosticChange
{
    [JsonStringEnumMemberName("added")] Added,
    [JsonStringEnumMemberName("removed")] Removed,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
    [JsonStringEnumMemberName("changed")] Changed,
}

[JsonConverter(typeof(StrictStringEnumConverter<FindingChange>))]
public enum FindingChange
{
    [JsonStringEnumMemberName("added")] Added,
    [JsonStringEnumMemberName("removed")] Removed,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
    [JsonStringEnumMemberName("evidence-changed")] EvidenceChanged,
}

[JsonConverter(typeof(StrictStringEnumConverter<IdentityState>))]
public enum IdentityState
{
    [JsonStringEnumMemberName("exact")] Exact,
    [JsonStringEnumMemberName("guessed")] Guessed,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
}

[JsonConverter(typeof(StrictStringEnumConverter<ExpectationAssertion>))]
public enum ExpectationAssertion
{
    [JsonStringEnumMemberName("correct-reading")] CorrectReading,
    [JsonStringEnumMemberName("whole-word-negative")] WholeWordNegative,
}

[JsonConverter(typeof(StrictStringEnumConverter<ReferenceAuthority>))]
public enum ReferenceAuthority
{
    [JsonStringEnumMemberName("human")] Human,
    [JsonStringEnumMemberName("source-validated")] SourceValidated,
}

[JsonConverter(typeof(StrictStringEnumConverter<ExpectationOutcome>))]
public enum ExpectationOutcome
{
    [JsonStringEnumMemberName("matched")] Matched,
    [JsonStringEnumMemberName("missing")] Missing,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(StrictStringEnumConverter<TraceOrigin>))]
public enum TraceOrigin
{
    [JsonStringEnumMemberName("stored")] Stored,
    [JsonStringEnumMemberName("diagnostic-rerun")] DiagnosticRerun,
}

[JsonConverter(typeof(StrictStringEnumConverter<ReadingAgreement>))]
public enum ReadingAgreement
{
    [JsonStringEnumMemberName("same")] Same,
    [JsonStringEnumMemberName("different")] Different,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(StrictStringEnumConverter<TargetKind>))]
public enum TargetKind
{
    [JsonStringEnumMemberName("resource")] Resource,
    [JsonStringEnumMemberName("tool")] Tool,
}

/// <summary>A closed union of TrialReviewResult variants.</summary>
[JsonConverter(typeof(TrialReviewResultConverter))]
public abstract record TrialReviewResult;

/// <summary>A closed union of FlagFacts variants.</summary>
[JsonConverter(typeof(FlagFactsConverter))]
public abstract record FlagFacts;

/// <summary>A closed union of ReviewRow variants.</summary>
[JsonConverter(typeof(ReviewRowConverter))]
public abstract record ReviewRow;

/// <summary>A closed union of EvidencePin variants.</summary>
[JsonConverter(typeof(EvidencePinConverter))]
public abstract record EvidencePin;

/// <summary>A factual review of frozen Trial evidence, with disclosure independent of its summary.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReportView(
    [property: JsonRequired] string Schema,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string ReportId,
    [property: JsonRequired] string ReportDigest,
    [property: JsonRequired] DetailLevel Detail,
    [property: JsonRequired] ReportPins Pins,
    [property: JsonRequired] Freshness Freshness,
    [property: JsonRequired] ReviewSummary Summary,
    [property: JsonRequired] IReadOnlyList<FlagEvidence> FlagEvidence,
    [property: JsonRequired] IReadOnlyList<Limitation> Limitations,
    [property: JsonRequired] DetailAvailability DetailAvailability,
    [property: JsonRequired] IReadOnlyList<SectionView> Sections,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReviewPage? Page,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] WordView? Word,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] TracePage? Trace,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Schema is null ||
            ReportId is null ||
            ReportDigest is null ||
            Pins is null ||
            Freshness is null ||
            Summary is null ||
            FlagEvidence is null ||
            FlagEvidence.Any(item => item is null) ||
            Limitations is null ||
            Limitations.Any(item => item is null) ||
            Sections is null ||
            Sections.Any(item => item is null) ||
            Links is null ||
            Links.Any(item => item is null) ||
            FlagEvidence.Count != 5 ||
            FlagEvidence.Select(item => item.Id).Distinct().Count() != 5 ||
            Schema != "motif.proposal-review-report" ||
            SchemaVersion != 1)
            throw new JsonException("Invalid ReportView.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReportPins(
    [property: JsonRequired] string ProjectKey,
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Label,
    [property: JsonRequired] string IntentDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Revision,
    [property: JsonRequired] IReadOnlyList<PrerequisitePin> Prerequisites,
    [property: JsonRequired] string TrialJobId,
    [property: JsonRequired]
    [property: JsonConverter(typeof(ReviewBaselineTokenConverter))] BaselineToken BaselineToken,
    [property: JsonRequired] CandidatePin Candidate,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DryRunPin? DryRun,
    [property: JsonRequired] IReadOnlyList<CheckpointPin> Checkpoints,
    [property: JsonRequired] long SetCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SetsDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ExpectationsDigest,
    [property: JsonRequired] WarningCheckPins WarningCheckIds,
    [property: JsonRequired] IReadOnlyList<ParsimonyPair> ParsimonyPairs,
    [property: JsonRequired] string EvidenceManifestDigest,
    [property: JsonRequired] string AlgorithmVersion) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ProjectKey is null ||
            ProposalId is null ||
            IntentDigest is null ||
            Prerequisites is null ||
            Prerequisites.Any(item => item is null) ||
            TrialJobId is null ||
            BaselineToken is null ||
            Candidate is null ||
            Checkpoints is null ||
            Checkpoints.Any(item => item is null) ||
            SetCount is < 0 or > 9007199254740991L ||
            WarningCheckIds is null ||
            ParsimonyPairs is null ||
            ParsimonyPairs.Any(item => item is null) ||
            EvidenceManifestDigest is null ||
            AlgorithmVersion is null)
            throw new JsonException("Invalid ReportPins.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrerequisitePin(
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired] string IntentDigest) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ProposalId is null ||
            IntentDigest is null)
            throw new JsonException("Invalid PrerequisitePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CandidatePin(
    [property: JsonRequired] string Identity,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GrammarDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ModelFingerprint) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Identity is null)
            throw new JsonException("Invalid CandidatePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DryRunPin(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string EffectDigest,
    [property: JsonRequired] string FootprintDigest) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            EffectDigest is null ||
            FootprintDigest is null)
            throw new JsonException("Invalid DryRunPin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckpointPin(
    [property: JsonRequired] Side Side,
    [property: JsonRequired] IReadOnlyList<string> AssessmentIds,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Assessor,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ParserVersion,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ParserDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CounterSemantics,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ScopeDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] TokeniserPin? Tokeniser) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (AssessmentIds is null ||
            AssessmentIds.Any(item => item is null))
            throw new JsonException("Invalid CheckpointPin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TokeniserPin(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Version) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Name is null ||
            Version is null)
            throw new JsonException("Invalid TokeniserPin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WarningCheckPins(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Before,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? After) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate() { }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsimonyPair(
    [property: JsonRequired] string Group,
    [property: JsonRequired] string MeasureId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? BeforeReportId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? AfterReportId) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Group is null ||
            MeasureId is null)
            throw new JsonException("Invalid ParsimonyPair.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Freshness(
    [property: JsonRequired] FreshnessRelation Relation,
    [property: JsonRequired] IReadOnlyList<string> Reasons) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Reasons is null ||
            Reasons.Any(item => item is null))
            throw new JsonException("Invalid Freshness.");
    }
}

/// <summary>Observed flags and measurements; missing evidence remains distinct from zero.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewSummary(
    [property: JsonRequired] string Notice,
    [property: JsonRequired] IReadOnlyList<FlagFact> Flags,
    [property: JsonRequired] IncompleteSummary Incomplete,
    [property: JsonRequired] WordformSummary Wordforms,
    [property: JsonRequired] ApprovedSummary Approved,
    [property: JsonRequired] CoverageSummary SampledTokens,
    [property: JsonRequired] CoverageSummary ParadigmCells,
    [property: JsonRequired] IReadOnlyList<ControlSummary> Controls,
    [property: JsonRequired] NegativeSummary Negatives,
    [property: JsonRequired] WorkChange ParserWork,
    [property: JsonRequired] WarningSummary Warnings,
    [property: JsonRequired] IReadOnlyList<ParsimonySummary> Parsimony,
    [property: JsonRequired] EffectsSummary Effects,
    [property: JsonRequired] IReadOnlyList<SectionCount> SectionCounts) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Notice is null ||
            Flags is null ||
            Flags.Any(item => item is null) ||
            Incomplete is null ||
            Wordforms is null ||
            Approved is null ||
            SampledTokens is null ||
            ParadigmCells is null ||
            Controls is null ||
            Controls.Any(item => item is null) ||
            Negatives is null ||
            ParserWork is null ||
            Warnings is null ||
            Parsimony is null ||
            Parsimony.Any(item => item is null) ||
            Effects is null ||
            SectionCounts is null ||
            SectionCounts.Any(item => item is null) ||
            Flags.Select(item => item.Id).Distinct().Count() != Flags.Count ||
            Flags.Select(item => (int)item.Id).Where((value, index) => index > 0 && value < (int)Flags[index - 1].Id).Any() ||
            Notice != "No red flags means only 'not bad', never 'good'.")
            throw new JsonException("Invalid ReviewSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WordformSummary(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Requested,
    [property: JsonRequired] Coverage Before,
    [property: JsonRequired] Coverage After,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NewNoParseWords,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NewApprovedLossWords) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Requested is < 0 or > 9007199254740991L ||
            Before is null ||
            After is null ||
            NewNoParseWords is < 0 or > 9007199254740991L ||
            NewApprovedLossWords is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid WordformSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovedSummary(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Expected,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? BeforeMatched,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? AfterMatched,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NewlyLostReadings,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? UnknownAfter) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Expected is < 0 or > 9007199254740991L ||
            BeforeMatched is < 0 or > 9007199254740991L ||
            AfterMatched is < 0 or > 9007199254740991L ||
            NewlyLostReadings is < 0 or > 9007199254740991L ||
            UnknownAfter is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid ApprovedSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CoverageSummary(
    [property: JsonRequired] long SetCount,
    [property: JsonRequired] Coverage Before,
    [property: JsonRequired] Coverage After,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? DeltaPoints) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (SetCount is < 0 or > 9007199254740991L ||
            Before is null ||
            After is null ||
            (DeltaPoints is double deltaPointsValue && !double.IsFinite(deltaPointsValue)))
            throw new JsonException("Invalid CoverageSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ControlSummary(
    [property: JsonRequired] string Role,
    [property: JsonRequired] long SetCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? WordCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ExpectedReadingCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? LostReadings,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? AdditionalReadings,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? UnresolvedExpectations) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Role is null ||
            SetCount is < 0 or > 9007199254740991L ||
            WordCount is < 0 or > 9007199254740991L ||
            ExpectedReadingCount is < 0 or > 9007199254740991L ||
            LostReadings is < 0 or > 9007199254740991L ||
            AdditionalReadings is < 0 or > 9007199254740991L ||
            UnresolvedExpectations is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid ControlSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NegativeSummary(
    [property: JsonRequired] long SetCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NewlyAcceptedWords,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? AlreadyAcceptedWords,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? UnknownWords) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (SetCount is < 0 or > 9007199254740991L ||
            NewlyAcceptedWords is < 0 or > 9007199254740991L ||
            AlreadyAcceptedWords is < 0 or > 9007199254740991L ||
            UnknownWords is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid NegativeSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WarningSummary(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NewCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ResolvedCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? PreExistingCount) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (NewCount is < 0 or > 9007199254740991L ||
            ResolvedCount is < 0 or > 9007199254740991L ||
            PreExistingCount is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid WarningSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsimonySummary(
    [property: JsonRequired] string Group,
    [property: JsonRequired] string MeasureId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NewFindings,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ResolvedFindings,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? EvidenceChangedFindings,
    [property: JsonRequired] string Unit,
    [property: JsonRequired] EvidenceState EvidenceState) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Group is null ||
            MeasureId is null ||
            NewFindings is < 0 or > 9007199254740991L ||
            ResolvedFindings is < 0 or > 9007199254740991L ||
            EvidenceChangedFindings is < 0 or > 9007199254740991L ||
            Unit is null)
            throw new JsonException("Invalid ParsimonySummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EffectsSummary(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ChangeCount,
    [property: JsonRequired] bool DryRunAvailable) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ChangeCount is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid EffectsSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SectionCount(
    [property: JsonRequired] string Section,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Total,
    [property: JsonRequired] string Unit,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Available) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Section is null ||
            Total is < 0 or > 9007199254740991L ||
            Unit is null ||
            Available is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid SectionCount.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FlagFact(
    [property: JsonRequired] ReviewFlagId Id,
    [property: JsonRequired] string Text,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? WordCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ReadingCount,
    [property: JsonRequired] EvidenceState EvidenceState,
    [property: JsonRequired] IReadOnlyList<string> SectionIds,
    [property: JsonRequired] IReadOnlyList<string> SampleEventIds,
    [property: JsonRequired] FlagFacts Facts) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Text is null ||
            WordCount is < 0 or > 9007199254740991L ||
            ReadingCount is < 0 or > 9007199254740991L ||
            SectionIds is null ||
            SectionIds.Any(item => item is null) ||
            SampleEventIds is null ||
            SampleEventIds.Any(item => item is null) ||
            Facts is null ||
            SampleEventIds.Count > 5 ||
            (Id == ReviewFlagId.NewWordFailures && Facts is not NewWordFacts) ||
            (Id == ReviewFlagId.ConfirmedReadingsLost && Facts is not ReadingLossFacts) ||
            (Id == ReviewFlagId.KnownNonwordsNowAccepted && Facts is not NegativeFacts) ||
            (Id == ReviewFlagId.ParserWorkIncrease && Facts is not WorkChange) ||
            (Id == ReviewFlagId.NewWarningsOrParsimony && Facts is not NewFindingFacts))
            throw new JsonException("Invalid FlagFact.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NewWordFacts(
    [property: JsonRequired] long NoParseWords,
    [property: JsonRequired] long LostApprovedWords,
    [property: JsonRequired] long OverlapWords) : FlagFacts, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "word-failures";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (NoParseWords is < 0 or > 9007199254740991L ||
            LostApprovedWords is < 0 or > 9007199254740991L ||
            OverlapWords is < 0 or > 9007199254740991L ||
            Kind != "word-failures")
            throw new JsonException("Invalid NewWordFacts.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadingLossFacts(
    [property: JsonRequired] long UniqueReadings,
    [property: JsonRequired] long ApprovedConfirmed,
    [property: JsonRequired] long ReferenceConfirmed,
    [property: JsonRequired] long OverlapConfirmed,
    [property: JsonRequired] long OverlapWithWordFailures) : FlagFacts, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "reading-loss";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (UniqueReadings is < 0 or > 9007199254740991L ||
            ApprovedConfirmed is < 0 or > 9007199254740991L ||
            ReferenceConfirmed is < 0 or > 9007199254740991L ||
            OverlapConfirmed is < 0 or > 9007199254740991L ||
            OverlapWithWordFailures is < 0 or > 9007199254740991L ||
            Kind != "reading-loss")
            throw new JsonException("Invalid ReadingLossFacts.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NegativeFacts(
    [property: JsonRequired] long Words,
    [property: JsonRequired] long Memberships,
    [property: JsonRequired] IReadOnlyList<string> SetIds) : FlagFacts, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "negative-accepted";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Words is < 0 or > 9007199254740991L ||
            Memberships is < 0 or > 9007199254740991L ||
            SetIds is null ||
            SetIds.Any(item => item is null) ||
            Kind != "negative-accepted")
            throw new JsonException("Invalid NegativeFacts.");
    }
}

/// <summary>Percent change over the same scope, or a reason the comparison is unavailable.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkChange(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? PercentChange,
    [property: JsonRequired] long WordCount,
    [property: JsonRequired] string ScopeDigest,
    [property: JsonRequired] WorkComparison Comparison,
    [property: JsonRequired] IReadOnlyList<string> ReasonCodes) : FlagFacts, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "parser-work";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if ((PercentChange is double percentChangeValue && !double.IsFinite(percentChangeValue)) ||
            WordCount is < 0 or > 9007199254740991L ||
            ScopeDigest is null ||
            ReasonCodes is null ||
            ReasonCodes.Any(item => item is null) ||
            Kind != "parser-work" ||
            (Comparison == WorkComparison.Comparable) != PercentChange.HasValue)
            throw new JsonException("Invalid WorkChange.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NewFindingFacts(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Warnings,
    [property: JsonRequired] IReadOnlyList<FindingCount> Parsimony) : FlagFacts, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "new-findings";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Warnings is < 0 or > 9007199254740991L ||
            Parsimony is null ||
            Parsimony.Any(item => item is null) ||
            Kind != "new-findings")
            throw new JsonException("Invalid NewFindingFacts.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FindingCount(
    [property: JsonRequired] string MeasureId,
    [property: JsonRequired] long Findings,
    [property: JsonRequired] string Unit) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (MeasureId is null ||
            Findings is < 0 or > 9007199254740991L ||
            Unit is null)
            throw new JsonException("Invalid FindingCount.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FlagEvidence(
    [property: JsonRequired] ReviewFlagId Id,
    [property: JsonRequired] EvidenceState State,
    [property: JsonRequired] IReadOnlyList<string> Reasons,
    [property: JsonRequired] long ObservedEvents) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Reasons is null ||
            Reasons.Any(item => item is null) ||
            ObservedEvents is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid FlagEvidence.");
    }
}

/// <summary>A rate with its denominator, unknown cases and precision rather than an acceptance target.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Coverage(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Numerator,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Denominator,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? KnownNoMatch,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Unknown,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Percent,
    [property: JsonRequired] CoveragePrecision Precision,
    [property: JsonRequired] IReadOnlyList<string> ReasonCodes) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Numerator is < 0 or > 9007199254740991L ||
            Denominator is < 0 or > 9007199254740991L ||
            KnownNoMatch is < 0 or > 9007199254740991L ||
            Unknown is < 0 or > 9007199254740991L ||
            (Percent is double percentValue && !double.IsFinite(percentValue)) ||
            ReasonCodes is null ||
            ReasonCodes.Any(item => item is null))
            throw new JsonException("Invalid Coverage.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IncompleteSummary(
    [property: JsonRequired] string Label,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? RequiredCases,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? AffectedWords,
    [property: JsonRequired] IncompleteSide Before,
    [property: JsonRequired] IncompleteSide After,
    [property: JsonRequired] IReadOnlyList<RunInterruption> RunInterruptions,
    [property: JsonRequired] long EvidenceGapCount) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Label is null ||
            RequiredCases is < 0 or > 9007199254740991L ||
            AffectedWords is < 0 or > 9007199254740991L ||
            Before is null ||
            After is null ||
            RunInterruptions is null ||
            RunInterruptions.Any(item => item is null) ||
            EvidenceGapCount is < 0 or > 9007199254740991L ||
            Label != "Incomplete")
            throw new JsonException("Invalid IncompleteSummary.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IncompleteSide(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NotMeasured,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Stopped,
    [property: JsonRequired] IReadOnlyList<IncompleteReason> Reasons) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (NotMeasured is < 0 or > 9007199254740991L ||
            Stopped is < 0 or > 9007199254740991L ||
            Reasons is null ||
            Reasons.Any(item => item is null))
            throw new JsonException("Invalid IncompleteSide.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IncompleteReason(
    [property: JsonRequired] string Reason,
    [property: JsonRequired] long Cases) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Reason is null ||
            Cases is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid IncompleteReason.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RunInterruption(
    [property: JsonRequired] string JobId,
    [property: JsonRequired] string Code,
    [property: JsonRequired] Side Side,
    [property: JsonRequired] string ScopeDigest,
    [property: JsonRequired] long AttributedStoppedCases,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? UnmeasuredCases) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (JobId is null ||
            Code is null ||
            ScopeDigest is null ||
            AttributedStoppedCases is < 0 or > 9007199254740991L ||
            UnmeasuredCases is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid RunInterruption.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Limitation(
    [property: JsonRequired] string Code,
    [property: JsonRequired] IReadOnlyList<string> SectionIds,
    [property: JsonRequired] string Text,
    [property: JsonRequired] IReadOnlyList<string> EvidenceRefs) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Code is null ||
            SectionIds is null ||
            SectionIds.Any(item => item is null) ||
            Text is null ||
            EvidenceRefs is null ||
            EvidenceRefs.Any(item => item is null))
            throw new JsonException("Invalid Limitation.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadTarget(
    [property: JsonRequired] TargetKind Kind,
    [property: JsonRequired] string Name,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Uri,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Tool,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReadArguments? Arguments) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Name is null ||
            (Kind == TargetKind.Resource && (Uri is null || Tool is not null || Arguments is not null)) ||
            (Kind == TargetKind.Tool && (Uri is not null || Tool is null || Arguments is null)))
            throw new JsonException("Invalid ReadTarget.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SectionView(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Label,
    [property: JsonRequired] string Definition,
    [property: JsonRequired] string ScopeDigest,
    [property: JsonRequired] SectionCounts Counts,
    [property: JsonRequired] IReadOnlyList<CauseGroup> Groups,
    [property: JsonRequired] IReadOnlyList<Limitation> Limitations,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            Label is null ||
            Definition is null ||
            ScopeDigest is null ||
            Counts is null ||
            Groups is null ||
            Groups.Any(item => item is null) ||
            Limitations is null ||
            Limitations.Any(item => item is null) ||
            Links is null ||
            Links.Any(item => item is null))
            throw new JsonException("Invalid SectionView.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SectionCounts(
    [property: JsonRequired] string Unit,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Total,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Observed,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Unknown) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Unit is null ||
            Total is < 0 or > 9007199254740991L ||
            Observed is < 0 or > 9007199254740991L ||
            Unknown is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid SectionCounts.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CauseGroup(
    [property: JsonRequired] string Cause,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Count,
    [property: JsonRequired] string Unit,
    [property: JsonRequired] IReadOnlyList<ReviewRow> Samples,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Remaining,
    [property: JsonRequired] ReadTarget Link) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>The number of sampled positions represented by the affected forms in this group.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SamplePositionsTotal { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (SamplePositionsTotal is < 0 or > 9007199254740991L || Cause is null ||
            Count is < 0 or > 9007199254740991L ||
            Unit is null ||
            Samples is null ||
            Samples.Any(item => item is null) ||
            Remaining is < 0 or > 9007199254740991L ||
            Link is null)
            throw new JsonException("Invalid CauseGroup.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewPage(
    [property: JsonRequired] string Section,
    [property: JsonRequired] string FilterDigest,
    [property: JsonRequired] string Unit,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? TotalInSection,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? TotalMatching,
    [property: JsonRequired] long Returned,
    [property: JsonRequired] long Limit,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    [property: JsonRequired] ListAvailability ListAvailability,
    [property: JsonRequired] IReadOnlyList<ReviewRow> Items) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Section is null ||
            FilterDigest is null ||
            Unit is null ||
            TotalInSection is < 0 or > 9007199254740991L ||
            TotalMatching is < 0 or > 9007199254740991L ||
            Returned is < 0 or > 9007199254740991L ||
            Limit is < 0 or > 9007199254740991L ||
            Items is null ||
            Items.Any(item => item is null) ||
            Limit is < 1 or > 200 ||
            Returned > Limit ||
            Items.Count != Returned)
            throw new JsonException("Invalid ReviewPage.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NoTrialResult(
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Next) : TrialReviewResult, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string State { get; init; } = "no-trial";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ProposalId is null ||
            Next is null ||
            Next.Any(item => item is null) ||
            State != "no-trial")
            throw new JsonException("Invalid NoTrialResult.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PendingTrialResult(
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired] string JobId,
    [property: JsonRequired] string JobState,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReportView? Report,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Next) : TrialReviewResult, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string State { get; init; } = "pending";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ProposalId is null ||
            JobId is null ||
            JobState is null ||
            Next is null ||
            Next.Any(item => item is null) ||
            State != "pending")
            throw new JsonException("Invalid PendingTrialResult.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AvailableTrialResult(
    [property: JsonRequired] ReportView Report) : TrialReviewResult, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string State { get; init; } = "report";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Report is null ||
            State != "report")
            throw new JsonException("Invalid AvailableTrialResult.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadArguments(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Proposal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Action = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Job = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Report = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DetailLevel? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Section = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Set = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReviewFlagId? Flag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Filter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Word = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Case = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Side? Side = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Trace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cursor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Limit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Words = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WordsFile = null,
    [property: JsonPropertyName("negative_words")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? NegativeWords = null,
    [property: JsonPropertyName("review_sets")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ReviewSets = null,
    [property: JsonPropertyName("wait_seconds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? WaitSeconds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Difference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Offset = null) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    internal void Validate()
    {
        if (Limit is < 1 or > 200 || Offset is < 0 or > 9007199254740991L || WaitSeconds is < 1 or > 300 ||
            Action is not null and not ("run" or "read" or "refresh-report") ||
            Filter is not null and not ("all" or "changed" or "unchanged" or "review" or "incomplete") ||
            (Words is not null && WordsFile is not null) || (Job is not null && Report is not null) ||
            (Word is not null && Case is not null) || (Trace == true && Word is null && Case is null) ||
            (Words?.Any(string.IsNullOrWhiteSpace) ?? false) || (NegativeWords?.Any(string.IsNullOrWhiteSpace) ?? false) ||
            (ReviewSets?.Any(string.IsNullOrWhiteSpace) ?? false))
            throw new JsonException("Invalid read target arguments.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetEvidencePin(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Role,
    [property: JsonRequired] string MembershipDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ExpectationDigest) : EvidencePin, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "set";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            Role is null ||
            MembershipDigest is null ||
            Kind != "set")
            throw new JsonException("Invalid SetEvidencePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeEvidencePin(
    [property: JsonRequired] string ScopeDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Assessor,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ParserDigest,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CounterSemantics) : EvidencePin, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "scope";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ScopeDigest is null ||
            Kind != "scope")
            throw new JsonException("Invalid ScopeEvidencePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckpointEvidencePin(
    [property: JsonRequired] Side Side,
    [property: JsonRequired] IReadOnlyList<string> AssessmentIds,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ScopeDigest) : EvidencePin, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "checkpoint";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (AssessmentIds is null ||
            AssessmentIds.Any(item => item is null) ||
            Kind != "checkpoint")
            throw new JsonException("Invalid CheckpointEvidencePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpectationEvidencePin(
    [property: JsonRequired] string Id,
    [property: JsonRequired] ReferenceAuthority Reference,
    [property: JsonRequired] string ActorId,
    [property: JsonRequired] string RevisionId) : EvidencePin, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "expectation";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            ActorId is null ||
            RevisionId is null ||
            Kind != "expectation")
            throw new JsonException("Invalid ExpectationEvidencePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckEvidencePin(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? BeforeId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? AfterId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Digest) : EvidencePin, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "check";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Kind != "check")
            throw new JsonException("Invalid CheckEvidencePin.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsimonyEvidencePin(
    [property: JsonRequired] string Group,
    [property: JsonRequired] string MeasureId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? BeforeReportId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? AfterReportId) : EvidencePin, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string Kind { get; init; } = "parsimony";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Group is null ||
            MeasureId is null ||
            Kind != "parsimony")
            throw new JsonException("Invalid ParsimonyEvidencePin.");
    }
}
