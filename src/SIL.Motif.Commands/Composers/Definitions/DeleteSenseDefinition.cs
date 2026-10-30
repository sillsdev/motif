using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Deletes a sense, top-level or sub-sense, that carries content and is kept elsewhere.</summary>
internal sealed class DeleteSenseDefinition : ComposerDefinition<DeleteSenseIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-sense";

    /// <inheritdoc />
    public override string Composer => "DeleteSense";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("owner", IdRole.Reference, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_sense", AgentTool: false);

    /// <inheritdoc />
    public override DeleteSenseIntent Parse(JsonElement authored) => DeleteSenseIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteSenseIntent intent) =>
        DeleteSenseComposer.Build(context.Cache, intent);
}
