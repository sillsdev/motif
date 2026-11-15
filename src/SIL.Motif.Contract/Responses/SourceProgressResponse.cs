using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>Eligible places in one indexed Source Passage.</summary>
public sealed record SourceProgressPassage(string Id, string Locator, int EligiblePlaces)
{
    /// <summary>Approval cannot be measured until its authority is established.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? ApprovedPlaces => null;
}

/// <summary>Eligible place totals from one published Source generation.</summary>
public sealed record SourceProgressResponse(string Source, string EvidenceRevision, int EligiblePlaces,
    IReadOnlyList<SourceProgressPassage> Passages, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor)
{
    /// <summary>Approval cannot be measured until its authority is established.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? ApprovedPlaces => null;
    /// <summary>Explains why approval counts are absent.</summary>
    public string ApprovedState => "unavailable";
}
