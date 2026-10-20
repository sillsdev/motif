using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Sets the part of speech of one existing stem analysis after checking its current value.</summary>
internal sealed class SetStemPartOfSpeechDefinition : ComposerDefinition<SetStemPartOfSpeechIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-stem-part-of-speech";

    /// <inheritdoc />
    public override string Composer => "SetStemPartOfSpeech";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("expectedPartOfSpeech", required: true, nullable: true),
        Field.Id("partOfSpeech", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_stem_part_of_speech", AgentTool: false);

    /// <inheritdoc />
    public override SetStemPartOfSpeechIntent Parse(JsonElement authored) => SetStemPartOfSpeechIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetStemPartOfSpeechIntent intent) =>
        SetStemPartOfSpeechComposer.Build(context.Cache, intent);
}
