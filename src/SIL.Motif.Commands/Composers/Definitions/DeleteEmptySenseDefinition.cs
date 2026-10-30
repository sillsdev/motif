using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>
/// Deletes an empty sense and its unused analysis. A part-of-speech-only analysis is allowed for a linked variant.
/// </summary>
internal sealed class DeleteEmptySenseDefinition : ComposerDefinition<DeleteEmptySenseIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-empty-sense";

    /// <inheritdoc />
    public override string Composer => "DeleteEmptySense";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("entry", IdRole.Reference, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_empty_sense");

    /// <inheritdoc />
    public override DeleteEmptySenseIntent Parse(JsonElement authored) => DeleteEmptySenseIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteEmptySenseIntent intent) =>
        DeleteEmptySenseComposer.Build(context.Cache, intent);
}
