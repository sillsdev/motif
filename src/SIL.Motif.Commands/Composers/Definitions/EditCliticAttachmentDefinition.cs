using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Sets a clitic stem analysis's Attaches to Categories to an exact set.</summary>
internal sealed class EditCliticAttachmentDefinition : ComposerDefinition<EditCliticAttachmentIntent>
{
    public override string Verb => "compose-edit-clitic-attachment";
    public override string Composer => "EditCliticAttachment";
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Ids("expectedCategories", required: true),
        Field.Ids("categories", required: true));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_clitic_attachment");
    public override EditCliticAttachmentIntent Parse(JsonElement authored) => EditCliticAttachmentIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditCliticAttachmentIntent intent) =>
        EditCliticAttachmentComposer.Build(context.Cache, intent);
}
