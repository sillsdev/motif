using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The bounded summary of a side-by-side comparison of Proposals. Every number is measured from the retained Trial
/// before/after evidence of each Proposal; nothing here is measured against approved analyses.
/// <c>Snapshot</c> is a digest of the exact Trial job IDs read, in column order; pass it back with a page.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalComparison(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Basis,
    [property: JsonRequired] string Snapshot,
    [property: JsonRequired] IReadOnlyList<CompareColumn> Columns,
    [property: JsonRequired] int FormsCompared,
    [property: JsonRequired] int FormsDiverging,
    [property: JsonRequired] int RowsTotal,
    [property: JsonRequired] int Pages,
    [property: JsonRequired] bool Complete,
    [property: JsonRequired] IReadOnlyList<CompareLimitation> Limitations)
{
    /// <summary>The value of <see cref="Basis"/>: each column's own Trial before and after, not approved analyses.</summary>
    public const string TrialBeforeAfter = "trial-before-after";

    /// <summary>The most Proposals one call may compare.</summary>
    public const int MaxProposals = 8;

    /// <summary>The most bytes the serialized summary may take.</summary>
    public const int SummaryByteLimit = 2000;

    /// <summary>The requested form rows, present only when the caller selects a page.</summary>
    public CompareRowPage? Page { get; init; }
}

/// <summary>
/// One Proposal's newest completed Trial, in the stable column order of the request. The counts are of forms; a form
/// can be both gained and lost, and the other groups are exclusive.
/// </summary>
/// <param name="ProposalId">The Proposal.</param>
/// <param name="Gained">Forms that gained at least one Reading that Trial's before side lacked.</param>
/// <param name="Lost">Forms that lost at least one Reading that Trial's before side had.</param>
/// <param name="Unchanged">Measured forms with no gain and no loss.</param>
/// <param name="NotMeasured">Forms in the comparison's domain this Trial did not measure on both sides.</param>
/// <param name="Incomplete">Measured forms whose search stopped early on either side.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareColumn(
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired] int Gained,
    [property: JsonRequired] int Lost,
    [property: JsonRequired] int Unchanged,
    [property: JsonRequired] int NotMeasured,
    [property: JsonRequired] int Incomplete);

/// <summary>A typed reason the totals may not mean what they seem to. Count is the number of columns it touches.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareLimitation([property: JsonRequired] string Code, [property: JsonRequired] int Count)
{
    /// <summary>Some column did not measure some forms on both sides; they never read as unchanged.</summary>
    public const string NotMeasured = "not-measured";

    /// <summary>Some Trials stopped early, so their gains and losses are partial.</summary>
    public const string IncompleteTrial = "incomplete-trial";
}

/// <summary>A page of form rows in the frozen order: diverging first, then writing system and form key.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareRowPage(
    [property: JsonRequired] int Number,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? NextPage,
    [property: JsonRequired] IReadOnlyList<CompareRow> Rows)
{
    /// <summary>The most rows in one page.</summary>
    public const int Size = 100;
}

/// <summary>One form and what each column observed for it, in column order. Reading IDs are ReadingSignature IDs.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareRow(
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WritingSystemId,
    [property: JsonRequired] string Form,
    [property: JsonRequired] string FormKey,
    [property: JsonRequired] bool Diverges,
    [property: JsonRequired] IReadOnlyList<CompareCell> Cells);

/// <summary>What one column's Trial observed for one form. A form the Trial did not measure is never unchanged.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareCell(
    [property: JsonRequired] bool Measured,
    [property: JsonRequired] bool Complete,
    [property: JsonRequired] IReadOnlyList<string> GainedIds,
    [property: JsonRequired] IReadOnlyList<string> LostIds);
