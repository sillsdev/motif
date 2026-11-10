using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Sets or replaces the Note of existing Text segments in bulk.</summary>
internal sealed class EditTextNotesDefinition : ComposerDefinition<EditTextNotesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-text-notes";
    /// <inheritdoc />
    public override string Composer => "EditTextNotes";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["texts-and-analyses"];
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("analysisWs"),
        Field.Nested("edits", IntentShape.Of(
            Field.Text("text", required: true),
            Field.Number("segment"),
            Field.Text("word"),
            Field.Text("note", required: true),
            Field.Text("at")).AtMostOne("segment", "word"), required: true, many: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, AgentTool: false);
    /// <inheritdoc />
    public override EditTextNotesIntent Parse(JsonElement authored) => EditTextNotesIntentParser.Parse(authored);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditTextNotesIntent intent) =>
        EditTextNotesComposer.Build(context.Cache, intent, context.MintId);
}
