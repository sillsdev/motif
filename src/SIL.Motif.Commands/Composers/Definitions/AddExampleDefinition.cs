using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>Adds a new example sentence, with optional translations, to a sense.</summary>
internal sealed class AddExampleDefinition : ComposerDefinition<AddExampleIntent>
{
    private static readonly IntentShape Forms = IntentShape.Of(Field.Text("ws", required: true), Field.Text("text", required: true));

    /// <inheritdoc />
    public override string Verb => "compose-add-example";

    /// <inheritdoc />
    public override string Composer => "AddExample";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("sense", IdRole.Target, required: true),
        Field.Nested("text", Forms, required: true, many: true),
        Field.Text("reference"),
        Field.Nested("translations", IntentShape.Of(
            Field.Choice("type", LexExampleCreatePayload.TranslationTypes),
            Field.Nested("text", Forms, required: true, many: true)), many: true),
        Field.Id("after", IdRole.Reference),
        Field.Id("before", IdRole.Reference)).AtMostOne("after", "before");

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_example", AgentTool: false);

    /// <inheritdoc />
    public override AddExampleIntent Parse(JsonElement authored) => AddExampleIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AddExampleIntent intent) =>
        AddExampleComposer.Build(context.Cache, intent, context.MintId);
}
