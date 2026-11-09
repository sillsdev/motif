using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Moves named characters of one writing system between word-forming and punctuation.</summary>
internal sealed class SetCharacterClassDefinition : ComposerDefinition<SetCharacterClassIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-character-class";

    /// <inheritdoc />
    public override string Composer => "SetCharacterClass";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("ws", required: true),
        Field.Choice("class", ["wordForming", "punctuation"], required: true),
        Field.Text("characters", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_character_class");

    /// <inheritdoc />
    public override SetCharacterClassIntent Parse(JsonElement authored) =>
        SetCharacterClassIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetCharacterClassIntent intent) =>
        SetCharacterClassComposer.Build(context.Cache, intent);
}
