using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>
/// Deletes one wholly empty sense of an entry together with the stem analysis only that sense uses. A sense with any
/// content is refused and no flag overrides that, because deleting it would lose a person's work.
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
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_empty_sense", AgentTool: false);

    /// <inheritdoc />
    public override DeleteEmptySenseIntent Parse(JsonElement authored) => DeleteEmptySenseIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteEmptySenseIntent intent) =>
        DeleteEmptySenseComposer.Build(context.Cache, intent);
}
