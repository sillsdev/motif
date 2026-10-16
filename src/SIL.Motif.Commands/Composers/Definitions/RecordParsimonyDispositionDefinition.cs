using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages one disposition of one Parsimony finding as a Notebook record for a person to Apply.</summary>
internal sealed class RecordParsimonyDispositionDefinition : ComposerDefinition<RecordParsimonyDispositionIntent>
{
    private static readonly IntentShape JudgmentObject = IntentShape.Of(
        Field.Text("class", required: true),
        Field.Id("id", IdRole.Reference, required: true),
        Field.Id("owningEntryId", IdRole.Reference));

    // One list for the edge and computed-group roles, taken from the enums the hand tool schema also names.
    private static readonly string[] RoleNames =
        [.. ClosedJudgmentEnumConverter<JudgmentEdgeRole>.Names, .. ClosedJudgmentEnumConverter<JudgmentGroupRole>.Names];

    // A shape cannot tell the five subject kinds apart, so the parser requires each kind's own fields.
    private static readonly IntentShape Subject = IntentShape.Of(
        Field.Choice("kind", ["object", "project", "edge", "computed-group", "parsimony-finding"], required: true),
        Field.Nested("object", JudgmentObject),
        Field.Text("id"),
        Field.Choice("role", RoleNames),
        Field.Nested("owner", JudgmentObject),
        Field.Nested("from", JudgmentObject),
        Field.Nested("to", JudgmentObject),
        Field.Text("measureId"),
        Field.Nested("members", JudgmentObject, many: true),
        Field.Text("findingId"));

    /// <summary>The authored form of an intent built in code, the one the parser reads back and a draft records.</summary>
    internal static JsonElement ToAuthored(RecordParsimonyDispositionIntent intent) =>
        JsonSerializer.SerializeToElement(new
        {
            recordTypeId = intent.RecordTypeId.Value,
            intent.MeasureId,
            intent.Subject,
            intent.Disposition,
            intent.EvidenceDigest,
            intent.EvidenceContract,
            intent.SubjectCaption,
            intent.MeasureCaption,
            intent.Reason,
            intent.Question,
            reportId = intent.ReportId?.Value,
        }, AuthoredOptions);

    private static readonly JsonSerializerOptions AuthoredOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new ClosedJudgmentEnumConverter<JudgmentEdgeRole>(),
            new ClosedJudgmentEnumConverter<JudgmentGroupRole>(),
        },
    };

    /// <inheritdoc />
    public override string Verb => "compose-record-parsimony-disposition";

    /// <inheritdoc />
    public override string Composer => "RecordParsimonyDisposition";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["parsimony-dispositions"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("recordTypeId", IdRole.Reference, required: true),
        Field.Text("measureId", required: true),
        Field.Nested("subject", Subject, required: true),
        Field.Choice("disposition", ["fix", "keep", "ask", "defer"], required: true),
        Field.Text("evidenceDigest", required: true),
        Field.Text("evidenceContract", required: true),
        Field.Text("subjectCaption", required: true),
        Field.Text("measureCaption", required: true),
        Field.Text("reason", nullable: true),
        Field.Text("question"),
        Field.Id("reportId", IdRole.Opaque));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = new(CommandSurface.AdvancedAi, AgentClass.Draft,
        OnByDefault: false, InBatch: false, ToolName: "motif_record_parsimony_disposition", HandWrittenTool: true);

    /// <inheritdoc />
    public override ReplacementRule? Replaces { get; } = ReplacementRule.SameIdentity(FindingIdentity,
        "An earlier intent in this batch already records a disposition of the same finding; record one.");

    /// <inheritdoc />
    public override RecordParsimonyDispositionIntent Parse(JsonElement authored) =>
        RecordParsimonyDispositionIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RecordParsimonyDispositionIntent intent) =>
        RecordParsimonyDispositionComposer.Build(context.Cache, intent, context.ProposalId, context.MintId);

    // A finding is its measure and exact subject; the choice and evidence are what a later disposition replaces.
    private static string? FindingIdentity(JsonElement authored)
    {
        try
        {
            var intent = RecordParsimonyDispositionIntentParser.Parse(authored);
            return intent.MeasureId + "|" + HumanJudgmentCodec.SubjectKey(intent.Subject);
        }
        catch (Exception exception) when (exception is ContractParseException or FormatException or
            ArgumentException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }
}
