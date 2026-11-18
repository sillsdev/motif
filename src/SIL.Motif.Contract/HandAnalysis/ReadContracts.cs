using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.HandAnalysis;

/// <summary>The complete identity and verification basis of one immutable publication.</summary>
/// <remarks>
/// ComparisonVersion identifies the exact ICU and collation basis. A comparer change invalidates its keys.
/// The Stamp identifies the point served. Currentness is a host comparison with the latest publication or a separate proof check.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SnapshotStamp(string SnapshotId, Guid ProjectId, string HomePoint,
    string SourceRevision, string ParserRevision, string SettingsRevision,
    string ComparisonVersion, int IndexSchema, SnapshotVerification Verification);

[JsonConverter(typeof(StrictStringEnumConverter<SnapshotVerification>))]
public enum SnapshotVerification
{
    Full,
    Certified
}

/// <summary>Request up to 200 rows. The cursor is opaque to callers and belongs to the query that issued it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PageRequest(int Limit = 50, string? Cursor = null)
{
    public const int MaxLimit = 200;
}

/// <summary>Continuation identity carried by an adapter's opaque cursor.</summary>
/// <remarks>
/// QueryKey identifies the family and complete selector, including order and filters, independent of page size.
/// LastOrderKey is the complete key of the last returned row, including the native tie breaker.
/// A token from another snapshot or query must be refused even if the order key exists in the current result.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CursorBinding(string SnapshotId, string QueryKey, string LastOrderKey);

/// <summary>Complete describes the facts and counts for the entire query, independent of whether more pages exist.</summary>
/// <remarks>
/// The issuing pin supplies the Stamp even on empty or limited pages. Use a null total and a limitation
/// when the exact total is unknown. A continuation cursor is validated even when evidence is limited.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Page<T>(SnapshotStamp Stamp, ImmutableArray<T> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Total, string? NextCursor,
    bool Complete, ImmutableArray<ReadLimitation> Limitations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadLimitation(string Code, ImmutableArray<Guid> AffectedIds, string Reason);

/// <summary>Stable facts for unavailable acquisitions and exact-basis resolution.</summary>
public static class HandAnalysisRefusalFacts
{
    /// <summary>The fact on IndexUnavailable giving the acquisition failure.</summary>
    public const string Cause = "cause";
    public const string Corrupt = "corrupt";
    public const string IncompleteNative = "incomplete-native";
    public const string Unavailable = "unavailable";
    public const string AmbiguousBasis = "ambiguous-basis";

    /// <summary>The fact on SnapshotExpired when the exact ID has already been resolved.</summary>
    public const string SnapshotId = "snapshotId";
}

public static class HandAnalysisVersions
{
    public const int OperationSetVersion = 1;
}
