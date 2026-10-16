using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Sets or clears the inflection class of one stem analysis.</summary>
internal sealed class EditStemInflectionClassDefinition : ComposerDefinition<EditStemInflectionClassIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-stem-inflection-class";

    /// <inheritdoc />
    public override string Composer => "EditStemInflectionClass";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("expectedInflectionClass", required: true, nullable: true),
        Field.Id("inflectionClass", required: true, nullable: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, AgentTool: false);

    /// <inheritdoc />
    public override EditStemInflectionClassIntent Parse(JsonElement authored) => EditStemInflectionClassIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditStemInflectionClassIntent intent) =>
        EditStemInflectionClassComposer.Build(context.Cache, intent);
}
