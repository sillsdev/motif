using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.HandAnalyses;

namespace SIL.Motif.Contract.HandAnalysis;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OccurrenceQuery.Exact), "exact")]
[JsonDerivedType(typeof(OccurrenceQuery.Range), "range")]
[JsonDerivedType(typeof(OccurrenceQuery.Members), "members")]
[JsonDerivedType(typeof(OccurrenceQuery.Uses), "uses")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record OccurrenceQuery
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Exact(OccurrenceRef Occurrence) : OccurrenceQuery;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Range(HandAnalysisBarePopulation Scope, HandAnalysisAnchor? From, HandAnalysisAnchor? Through,
        OccurrenceDirection Direction, ImmutableArray<LineSlot> MissingVisibleSlots) : OccurrenceQuery;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Members(OccurrenceScope Scope, ImmutableArray<WordformKey> Wordforms,
        ImmutableArray<OccurrenceState> States) : OccurrenceQuery;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Uses(ImmutableArray<ReferenceTarget> Targets, OccurrenceScope? Scope = null) : OccurrenceQuery;
}

/// <summary>An Occurrence address with the original binding and wordform when supplied by the caller.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccurrenceRef(HandAnalysisAnchor Anchor, OccurrenceReadBinding? Binding = null,
    HandAnalysisWordform? Wordform = null);

/// <summary>Detached compare-and-set facts from the established occurrence binding.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccurrenceReadBinding(Guid ProjectGuid, string HomeRevision, ImmutableArray<string> OccurrenceHeads,
    string ContextHash, string SegmentationId, Guid? AssignmentId = null, bool? ParseIsCurrent = null)
{
    /// <summary>Copies the annotation heads so later caller edits cannot change the read binding.</summary>
    public static OccurrenceReadBinding FromExpected(HandAnalysisExpected expected) =>
        new(expected.ProjectGuid, expected.HomeRevision, [.. expected.OccurrenceHeads], expected.ContextHash,
            expected.SegmentationId, expected.AssignmentId, expected.ParseIsCurrent);
}

/// <summary>Scope is resolved against the Source revision of the pinned snapshot.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OccurrenceScope.ChapterOrText), "chapter-or-text")]
[JsonDerivedType(typeof(OccurrenceScope.Corpus), "corpus")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record OccurrenceScope
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ChapterOrText(HandAnalysisBarePopulation Population) : OccurrenceScope;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Corpus(HandAnalysisCorpusPopulation Population) : OccurrenceScope;
}

[JsonConverter(typeof(StrictStringEnumConverter<OccurrenceDirection>))]
public enum OccurrenceDirection
{
    Forward,
    Backward
}

[JsonConverter(typeof(StrictStringEnumConverter<OccurrenceState>))]
public enum OccurrenceState
{
    Bare,
    Analysis,
    Gloss,
    Conflict,
    Stale,
    Unavailable,
    Punctuation
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LineSlot(string Kind, string Ws);

/// <summary>Preserves known occurrence membership and native facts when the binding or wordform is unavailable.</summary>
/// <remarks>Use row and page limitations for missing facts. Refuse with SourceUnavailable or IndexUnavailable
/// when the Source or index cannot establish membership.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccurrenceFact(SnapshotStamp Stamp, HandAnalysisAnchor Anchor, OccurrenceReadBinding? Binding,
    HandAnalysisWordform? Wordform, bool? SentenceInitial, long Position, bool? SegmentStart, bool? SegmentEnd,
    OccurrenceState State, OccurrenceAttachment? Attachment, Guid? ResolvedWordformId, AnalysisRef? ResolvedAnalysis,
    Guid? ResolvedGlossId, ImmutableArray<ApprovalHead> ApprovalHeads, ImmutableArray<ApprovalSettle> Settles,
    GuessFact? CachedGuess, bool SourceAvailable, bool? ParseIsCurrent, TranslationDestination? Translation,
    ImmutableDictionary<Guid, string> Fingerprints, ImmutableArray<LineSlot> FilledSlots, ImmutableArray<ReadLimitation> Limitations);

[JsonConverter(typeof(StrictStringEnumConverter<AttachmentKind>))]
public enum AttachmentKind
{
    Wordform,
    Analysis,
    Gloss,
    Punctuation
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccurrenceAttachment(AttachmentKind Kind, Guid Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalHead(string Id, AnalysisRef? Analysis, Guid? GlossId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalSettle(string Id, ImmutableArray<string> Heads, string? SelectedHead);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TranslationDestination(Guid RecordId, string Field, string Ws);

[JsonConverter(typeof(StrictStringEnumConverter<GuessOrigin>))]
public enum GuessOrigin
{
    Human,
    Grammar
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessFact(AnalysisRef Analysis, Guid? GlossId, GuessOrigin Origin, string Provenance);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccurrencePage(Page<OccurrenceFact> Rows, OccurrenceCounts Counts)
{
    /// <summary>The Stamp of the nested page.</summary>
    [JsonIgnore]
    public SnapshotStamp Stamp => Rows.Stamp;
}

/// <summary>Query-wide counts independent of page size. Unknown counts are null.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccurrenceCounts(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Bare,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Analysis,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Gloss,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Conflict,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Stale,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Unavailable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Punctuation, bool Complete);
