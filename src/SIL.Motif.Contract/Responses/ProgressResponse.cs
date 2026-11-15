using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>Counts one mutually exclusive classification of tokens or distinct types.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressBuckets(int Total, int Approved, int Misspelled, int Unmeasured,
    IReadOnlyDictionary<string, int> Reasons)
{
    /// <summary>Parser guesses are unavailable until compatible parser evidence is integrated.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? ParserGuess => null;
    /// <summary>Absence of analysis is unavailable until a complete parser case is integrated.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? NoAnalysis => null;
}

/// <summary>Token and exact-union type counts for a population.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressCounts(int Passages, ProgressBuckets Tokens, ProgressBuckets Types);

/// <summary>The policies and excluded places represented by the response.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressCoverage(string AnchorPolicy, string EligibilityPolicy, string TypePolicy, bool Complete,
    int ExcludedPlaces, IReadOnlyDictionary<string, int> Reasons);

/// <summary>The separate saved-content and judgement identities represented by the projection.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressFreshness(string Content, string Judgements, bool JudgementsCurrent)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? ParserTrial => null;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? ParserBundle => null;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool? ParserIsStale => null;
}

/// <summary>Changed-reading evidence is not yet available for these Texts.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressChangedSince(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SinceRevision, bool Complete = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? Passages => null;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? Events => null;
}

/// <summary>One FieldWorks Text, counted as a Passage.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressPassage(Guid Id, string Title, ProgressBuckets Tokens, ProgressBuckets Types,
    ProgressChangedSince ChangedSince)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool? Checked => null;
}

/// <summary>Progress from one atomic revision, with unavailable facts explicitly null.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressResponse(string Revision, string ScopeDigest, ProgressCoverage Coverage,
    ProgressFreshness Freshness, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ProgressCounts? Totals,
    IReadOnlyList<ProgressPassage> Rows, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    IReadOnlyList<string> Limitations, ProgressChangedSince ChangedSince)
{
    public int SchemaVersion => 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool? Checked => null;
    public IReadOnlyList<object> Groups => [];
}
