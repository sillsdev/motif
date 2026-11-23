using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The bounded summary of a side-by-side comparison of Proposals. Gained, lost and unchanged forms are measured from the
/// retained Trial before/after evidence of each Proposal; the approved counts compare the project's human-approved
/// analyses with those same Trial readings. <c>Snapshot</c> is a digest of the exact Trial job IDs and the approved
/// analyses' snapshot read, in column order; pass it back with a page.
/// <c>ApprovedSkippedOtherAgents</c> counts analyses approved only by a human agent other than the default user agent;
/// they are not counted as approved. <c>ApprovedTotal</c> is every approved analysis of the Baseline
/// (<c>IWfiWordform.HumanApprovedAnalyses</c>); <c>ApprovedUncounted</c> is those no column can count, because the analysis
/// has no Reading identity or no Trial measured its word on both sides.
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
    [property: JsonRequired] IReadOnlyList<CompareLimitation> Limitations,
    [property: JsonRequired] int ApprovedSkippedOtherAgents,
    [property: JsonRequired] int ApprovedTotal,
    [property: JsonRequired] int ApprovedUncounted)
{
    /// <summary>The value of <see cref="Basis"/>: gains and losses come from each column's own Trial before and after.</summary>
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
/// <param name="ApprovedKept">Approved analyses whose word this Trial's after side still yields, by <c>ReadingSignature.ApprovalKept</c>.</param>
/// <param name="ApprovedLost">Approved analyses this Trial's before side yielded and its after side no longer does.</param>
[JsonConverter(typeof(CompareColumn.CompactConverter))]
public sealed record CompareColumn(
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired] int Gained,
    [property: JsonRequired] int Lost,
    [property: JsonRequired] int Unchanged,
    [property: JsonRequired] int NotMeasured,
    [property: JsonRequired] int Incomplete,
    [property: JsonRequired] int ApprovedKept,
    [property: JsonRequired] int ApprovedLost)
{
    private static readonly string[] Names =
        ["proposalId", "gained", "lost", "unchanged", "notMeasured", "incomplete", "approvedKept", "approvedLost"];

    /// <summary>Writes each column on one line so eight columns of five-digit counts stay within the summary's byte limit.</summary>
    internal sealed class CompactConverter : JsonConverter<CompareColumn>
    {
        public override CompareColumn Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != Names.Length)
                throw new JsonException("A comparison column has exactly its eight members.");
            int Count(string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
                ? number : throw new JsonException($"The column member {name} must be an integer.");
            return new(root.TryGetProperty(Names[0], out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()!
                    : throw new JsonException("The column needs a Proposal ID."),
                Count(Names[1]), Count(Names[2]), Count(Names[3]), Count(Names[4]), Count(Names[5]), Count(Names[6]), Count(Names[7]));
        }

        public override void Write(Utf8JsonWriter writer, CompareColumn value, JsonSerializerOptions options)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var line = new Utf8JsonWriter(buffer))
            {
                line.WriteStartObject();
                line.WriteString(Names[0], value.ProposalId);
                line.WriteNumber(Names[1], value.Gained);
                line.WriteNumber(Names[2], value.Lost);
                line.WriteNumber(Names[3], value.Unchanged);
                line.WriteNumber(Names[4], value.NotMeasured);
                line.WriteNumber(Names[5], value.Incomplete);
                line.WriteNumber(Names[6], value.ApprovedKept);
                line.WriteNumber(Names[7], value.ApprovedLost);
                line.WriteEndObject();
            }
            writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
        }
    }
}

/// <summary>A typed reason the totals may not mean what they seem to. Count is the number of columns it touches.</summary>
[JsonConverter(typeof(CompareLimitation.CompactConverter))]
public sealed record CompareLimitation([property: JsonRequired] string Code, [property: JsonRequired] int Count)
{
    /// <summary>Names the versions or the revision behind the limitation, when the code needs more than a count.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    /// <summary>Writes each limitation on one line so seven of them stay within the summary's byte limit.</summary>
    internal sealed class CompactConverter : JsonConverter<CompareLimitation>
    {
        public override CompareLimitation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            string? code = null, detail = null;
            int? count = null;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("A limitation is an object.");
            foreach (var member in root.EnumerateObject())
                switch (member.Name)
                {
                    case "code" when member.Value.ValueKind == JsonValueKind.String: code = member.Value.GetString(); break;
                    case "count" when member.Value.TryGetInt32(out var number): count = number; break;
                    case "detail" when member.Value.ValueKind == JsonValueKind.String: detail = member.Value.GetString(); break;
                    default: throw new JsonException($"The limitation member {member.Name} is not valid.");
                }
            return new(code ?? throw new JsonException("The limitation needs a code."),
                count ?? throw new JsonException("The limitation needs a count.")) { Detail = detail };
        }

        public override void Write(Utf8JsonWriter writer, CompareLimitation value, JsonSerializerOptions options)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var line = new Utf8JsonWriter(buffer))
            {
                line.WriteStartObject();
                line.WriteString("code", value.Code);
                line.WriteNumber("count", value.Count);
                if (value.Detail is not null) line.WriteString("detail", value.Detail);
                line.WriteEndObject();
            }
            writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
        }
    }

    /// <summary>Some column did not measure some forms on both sides; they never read as unchanged.</summary>
    public const string NotMeasured = "not-measured";

    /// <summary>Some Trials stopped early, so their gains and losses are partial.</summary>
    public const string IncompleteTrial = "incomplete-trial";

    /// <summary>Some approved analyses were left out of the approved counts because only another human agent approved them.</summary>
    public const string ApprovedOtherAgents = "approved-other-agents";

    /// <summary>Some approved analyses count in no column: they have no Reading identity, or no Trial measured their word.</summary>
    public const string ApprovedUncounted = "approved-uncounted";

    /// <summary>Some Trial measured a Proposal revision the Proposal no longer has.</summary>
    public const string TrialStale = "trial-stale";

    /// <summary>Some Proposal has a newer Trial attempt than the one compared, or has itself been superseded.</summary>
    public const string TrialSuperseded = "trial-superseded";

    /// <summary>The PanGloss version differs between a Trial's two sides or between Trials; <c>Detail</c> names the versions.</summary>
    public const string ParserVersionDiffers = "parser-version-differs";
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

/// <summary>
/// What one column's Trial observed for one form. A form the Trial did not measure is never unchanged. The approved
/// IDs are the approved analyses' own ReadingSignature IDs, sense included.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareCell(
    [property: JsonRequired] bool Measured,
    [property: JsonRequired] bool Complete,
    [property: JsonRequired] IReadOnlyList<string> GainedIds,
    [property: JsonRequired] IReadOnlyList<string> LostIds,
    [property: JsonRequired] IReadOnlyList<string> ApprovedKeptIds,
    [property: JsonRequired] IReadOnlyList<string> ApprovedLostIds);
