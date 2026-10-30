using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Moves an example sentence, with its translations, to another sense.</summary>
internal sealed class MoveExampleDefinition : ComposerDefinition<MoveExampleIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-move-example";

    /// <inheritdoc />
    public override string Composer => "MoveExample";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("example", IdRole.Target, required: true),
        Field.Id("toSense", IdRole.Reference, required: true),
        Field.Id("after", IdRole.Reference),
        Field.Id("before", IdRole.Reference)).AtMostOne("after", "before");

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_move_example", AgentTool: false);

    /// <inheritdoc />
    public override MoveExampleIntent Parse(JsonElement authored) => MoveExampleIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MoveExampleIntent intent) =>
        MoveExampleComposer.Build(context.Cache, intent);
}
