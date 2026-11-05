using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages a guarded edit of one existing vernacular form alternative.</summary>
internal sealed class EditAllomorphFormDefinition : ComposerDefinition<EditAllomorphFormIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-allomorph-form";
    /// <inheritdoc />
    public override string Composer => "EditAllomorphForm";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Target, required: true),
        Field.Id("target", IdRole.Target, required: true),
        Field.Text("writingSystem", required: true),
        Field.Text("expectedForm", required: true),
        Field.Text("newForm", required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_allomorph_form");
    /// <inheritdoc />
    public override EditAllomorphFormIntent Parse(JsonElement authored) => EditAllomorphFormIntentParser.Parse(authored);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditAllomorphFormIntent intent) =>
        EditAllomorphFormComposer.Build(context.Cache, intent);
}
