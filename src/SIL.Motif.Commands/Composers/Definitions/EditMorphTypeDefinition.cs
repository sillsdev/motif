using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Changes an existing form's morph type within the stem family.</summary>
internal sealed class EditMorphTypeDefinition : ComposerDefinition<EditMorphTypeIntent>
{
    public override string Verb => "compose-edit-morph-type";
    public override string Composer => "EditMorphType";
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("morphType", IdRole.Reference, required: true));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_morph_type");
    public override EditMorphTypeIntent Parse(JsonElement authored) => EditMorphTypeIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditMorphTypeIntent intent) =>
        EditMorphTypeComposer.Build(context.Cache, intent);
}
