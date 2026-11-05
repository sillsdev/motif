using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages a guarded edit of one existing vernacular form alternative.</summary>
internal sealed class EditCitationFormDefinition : ComposerDefinition<EditCitationFormIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-citation-form";
    /// <inheritdoc />
    public override string Composer => "EditCitationForm";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Text("writingSystem", required: true),
        Field.Text("expectedForm", required: true),
        Field.Text("newForm", required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_citation_form");
    /// <inheritdoc />
    public override EditCitationFormIntent Parse(JsonElement authored) => EditCitationFormIntentParser.Parse(authored);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditCitationFormIntent intent) =>
        EditCitationFormComposer.Build(context.Cache, intent);
}
