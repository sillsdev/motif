using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>Eligible places in one indexed Source Passage.</summary>
/// <param name="Id">The Passage id.</param>
/// <param name="Locator">The Passage as people name it, such as <c>MAT 1</c>.</param>
/// <param name="EligiblePlaces">The places that can be approved.</param>
/// <param name="ApprovedPlaces">The approved places; null when the Source's kind does not measure approval.</param>
public sealed record SourceProgressPassage(string Id, string Locator, int EligiblePlaces,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ApprovedPlaces);

/// <summary>An Occurrence approved with more than one analysis, as Send/Receive leaves it when two people disagreed.</summary>
/// <param name="Anchor">The Occurrence location, such as <c>MAT 5:3#2</c>.</param>
/// <param name="Analyses">The analyses it is approved with; none of them is counted until someone approves again.</param>
public sealed record SourceApprovalConflict(string Anchor, IReadOnlyList<string> Analyses);

/// <summary>Eligible place totals from one published Source generation.</summary>
/// <param name="Source">The Source id.</param>
/// <param name="EvidenceRevision">The Source index generation the counts come from.</param>
/// <param name="EligiblePlaces">The places that can be approved.</param>
/// <param name="ApprovedPlaces">The approved places; null when <see cref="ApprovedState"/> is <c>unavailable</c>.</param>
/// <param name="Passages">A page of Passages, when asked for.</param>
/// <param name="NextCursor">Where the next page starts; null on the last.</param>
public sealed record SourceProgressResponse(string Source, string EvidenceRevision, int EligiblePlaces,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ApprovedPlaces,
    IReadOnlyList<SourceProgressPassage> Passages, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor)
{
    /// <summary>
    /// <c>available</c>; <c>unreadable</c> when some stored approvals could not be read, which are not counted as
    /// approved; or <c>unavailable</c> when the Source's kind does not measure approval, as for FieldWorks Texts.
    /// </summary>
    public string ApprovedState { get; init; } = "available";

    /// <summary>The Passages, or item names, whose stored approvals could not be read.</summary>
    public IReadOnlyList<string> UnreadableApprovals { get; init; } = [];

    /// <summary>The Passages with stored approvals that the Source no longer has; those are not counted as approved.</summary>
    public IReadOnlyList<string> OrphanedApprovals { get; init; } = [];

    /// <summary>The Occurrences approved with more than one analysis; they are shown and not counted as approved.</summary>
    public IReadOnlyList<SourceApprovalConflict> ConflictedApprovals { get; init; } = [];
}
