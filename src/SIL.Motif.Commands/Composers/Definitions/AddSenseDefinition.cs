using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Adds a sense to an existing entry, sharing one of its analyses, so a second meaning needs no homograph.</summary>
internal sealed class AddSenseDefinition : ComposerDefinition<AddSenseIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-add-sense";

    /// <inheritdoc />
    public override string Composer => "AddSense";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Target, required: true),
        Field.Text("gloss", required: true),
        Field.Text("analysisWs", required: true),
        Field.Text("definition"),
        Field.Id("analysis", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_sense");

    /// <inheritdoc />
    public override AddSenseIntent Parse(JsonElement authored) => AddSenseIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AddSenseIntent intent) =>
        AddSenseComposer.Build(context.Cache, intent, context.MintId);
}
