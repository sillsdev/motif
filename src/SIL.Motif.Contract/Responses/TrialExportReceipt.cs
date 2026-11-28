using SIL.Motif.Contract.Assess;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The bounded receipt returned by either surface for a retained Trial export.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialExportReceipt(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string JobId,
    [property: JsonRequired] string Path,
    [property: JsonRequired] long Bytes,
    [property: JsonRequired] string Sha256,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReportId,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReportDigest,
    [property: JsonRequired] TrialExportDenominators Denominators,
    [property: JsonRequired] string ProposalRevision,
    [property: JsonRequired] string BaselineBundle,
    [property: JsonRequired] long PublishMilliseconds,
    [property: JsonRequired] bool ComparisonComplete,
    [property: JsonRequired] bool PathIsStoreSuffix)
{
    /// <summary>The requested words, present only when the caller gives a cursor or a limit.</summary>
    public TrialExportPage? Page { get; init; }

    /// <summary>The per-word parser work limit the Trial ran under; absent for a Trial that froze none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ParserWorkLimit? PerWordParserWorkLimit { get; init; }
}

/// <summary>A page in the retained export's frozen word order.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialExportPage(
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    [property: JsonRequired] IReadOnlyList<JsonElement> Words)
{
    /// <summary>The word rows in a page when the caller gives no limit, and the spacing of the stored page offsets.</summary>
    public const int Size = 100;
}

/// <summary>Counts over frozen Trial cases, their expanded forms, and completed candidate searches.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialExportDenominators(
    [property: JsonRequired] int Submitted,
    [property: JsonRequired] int Expanded,
    [property: JsonRequired] int Finished,
    [property: JsonRequired] TrialExportSideCounts Before,
    [property: JsonRequired] TrialExportSideCounts After);

/// <summary>Expanded-form counts for one side of the Trial.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialExportSideCounts([property: JsonRequired] int Dispatched, [property: JsonRequired] int Finished);
