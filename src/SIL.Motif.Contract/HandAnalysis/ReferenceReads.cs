using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.HandAnalyses;

namespace SIL.Motif.Contract.HandAnalysis;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ReferenceQuery.Uses), "uses")]
[JsonDerivedType(typeof(ReferenceQuery.DeleteClosure), "delete-closure")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record ReferenceQuery
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Uses(ImmutableArray<ReferenceTarget> Targets, bool IncludeDescendants,
        OccurrenceScope? Scope = null) : ReferenceQuery;

    /// <summary>Return the authoritative deletion preview for this typed target, including its full owning closure.</summary>
    /// <remarks>
    /// The deletion kind is part of the cursor selector. Validate the target's native class, including on the
    /// first page. A known class that disagrees with the deletion kind gives <c>hand-analysis.type-mismatch</c>.
    /// </remarks>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DeleteClosure(DeletionTarget Target) : ReferenceQuery;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceTarget(ReferenceKind Kind, Guid Id);

[JsonConverter(typeof(StrictStringEnumConverter<ReferenceKind>))]
public enum ReferenceKind
{
    Wordform,
    Analysis,
    Gloss,
    Bundle,
    Entry,
    Sense,
    Allomorph,
    Msa,
    EntryRef,
    InflectionalType
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeletionTarget(DeletionKind Kind, Guid Id);

[JsonConverter(typeof(StrictStringEnumConverter<DeletionKind>))]
public enum DeletionKind
{
    Entry,
    Sense,
    Allomorph
}

/// <summary>Native and evidence edges to a target, including a descendant outside the query selector classes.</summary>
/// <remarks>A witness-only row can have no native source or field.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceFact(SnapshotStamp Stamp, ReferenceResultTarget Target, ReferenceResultTarget? Referrer,
    ReferenceLocation? Location, Guid? BundleId, Guid? AnalysisId, Guid? WordformId,
    ImmutableArray<ReferenceWitness> OccurrenceWitnesses, ReferenceState State);

/// <summary>The actual native target or source of an edge. NativeClass is open to all stored classes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceResultTarget(Guid Id, string? NativeClass);

/// <summary>Native field and optional zero-based vector position or rich-text run in a writing system.</summary>
/// <remarks>MemberId is opaque and can distinguish repeated references to the same native object.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceLocation(string Field, int? Position, string? MemberId, string? Ws, int? Run);

/// <summary>A local evidence witness with an opaque identity, revision and original location.</summary>
/// <remarks>Use Anchor for a proved typed address and retain RawLocation for evidence with only a literal address.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceWitness(string Kind, string Id, string EvidenceRevision, ReferenceResultTarget Target,
    ReferenceResultTarget? NativeSource, string? RawLocation, HandAnalysisAnchor? Anchor);

[JsonConverter(typeof(StrictStringEnumConverter<ReferenceState>))]
public enum ReferenceState
{
    Current,
    Unknown,
    Conflicting
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferencePage(Page<ReferenceFact> Rows, ReferenceCounts Counts)
{
    /// <summary>The Stamp of the nested page.</summary>
    [JsonIgnore]
    public SnapshotStamp Stamp => Rows.Stamp;
}

/// <summary>Distinct query-wide bundle, analysis and occurrence totals and delete preview evidence, repeated on every page.</summary>
/// <remarks>
/// Keep individual relationships for direct analysis anchors and their derived lexical edges. Count an occurrence witness
/// once by its identity in its evidence domain and revision. Include query-wide coverage limitations on every page.
/// PlanToken is the opaque acknowledgement for complete authoritative DeleteClosure evidence. Preparation and commit
/// compare the same canonical plan under the mutation lease and write lock. The acknowledged commit clears just the
/// acknowledged bundle-field links. Merged deletes retain dangling links. Incomplete coverage leaves PlanToken null
/// and deletion unavailable. ClosureDigest may carry diagnostics for incomplete coverage.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceCounts(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Bundles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Analyses,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Occurrences,
    ImmutableArray<TargetUsage> Usage, string? ClosureDigest, bool CountComplete, string? PlanToken);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TargetUsage(ReferenceResultTarget Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Analyses,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Glosses,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Senses,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? DistinctAnalysisAllomorphFrequency);
