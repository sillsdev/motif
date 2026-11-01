using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Stages a decision a named program made from cited sources, with the evidence it rests on.</summary>
internal sealed class RecordSourceValidatedDecisionDefinition : ComposerDefinition<RecordSourceValidatedDecisionIntent>
{
    private static readonly IntentShape EvidenceSource = IntentShape.Of(
        Field.Choice("kind", DecisionEvidence.SourceKinds, required: true),
        Field.Text("source", required: true),
        Field.Text("locator"));

    private static readonly IntentShape EvidenceDetail = IntentShape.Of(
        Field.Text("intendedForm"),
        Field.Text("editType"),
        Field.Text("violatedRule"),
        Field.Text("standardForm"),
        Field.Number("corpusCount"));

    /// <inheritdoc />
    public override string Verb => "compose-record-source-validated-decision";

    /// <inheritdoc />
    public override string Composer => "RecordSourceValidatedDecision";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["texts-and-analyses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Nested("agent", IntentShape.Of(Field.Text("name", required: true), Field.Text("version", required: true))),
        Field.Choice("member", ["approves", "disapproves"]),
        Field.Nested("evidence", IntentShape.Of(
            Field.Choice("tier", DecisionEvidence.Tiers, required: true),
            Field.Nested("sources", EvidenceSource, many: true),
            Field.Nested("detail", EvidenceDetail),
            Field.Text("stagedBy")), required: true)).Together("agent", "member");

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_record_source_validated_decision", AgentTool: false);

    /// <inheritdoc />
    public override RecordSourceValidatedDecisionIntent Parse(JsonElement authored) =>
        RecordSourceValidatedDecisionIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RecordSourceValidatedDecisionIntent intent) =>
        RecordSourceValidatedDecisionComposer.Build(context.Cache, intent);
}
