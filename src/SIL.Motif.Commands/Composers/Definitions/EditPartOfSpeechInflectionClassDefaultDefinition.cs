using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Edits the explicit default inflection class of a part of speech.</summary>
internal sealed class EditPartOfSpeechInflectionClassDefaultDefinition : ComposerDefinition<EditPartOfSpeechInflectionClassDefaultIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-part-of-speech-inflection-class-default";

    /// <inheritdoc />
    public override string Composer => "EditPartOfSpeechInflectionClassDefault";

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("expectedInflectionClass", required: true, nullable: true),
        Field.Id("inflectionClass", required: true, nullable: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, OnByDefault: false, AgentTool: false);

    /// <inheritdoc />
    public override EditPartOfSpeechInflectionClassDefaultIntent Parse(JsonElement authored) => EditPartOfSpeechInflectionClassDefaultIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditPartOfSpeechInflectionClassDefaultIntent intent) =>
        EditPartOfSpeechInflectionClassDefaultComposer.Build(context.Cache, intent);
}
