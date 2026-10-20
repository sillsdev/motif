using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>
/// Adds written representations to, or respells those of, one existing phoneme, so a multigraph such as ng, ts or
/// dy is another way the same segment is written, not a second segment.
/// </summary>
internal sealed class EditPhonemeDefinition : ComposerDefinition<EditPhonemeIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-phoneme";

    /// <inheritdoc />
    public override string Composer => "EditPhoneme";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Nested("add", IntentShape.Of(Field.Text("representation", required: true)), many: true),
        Field.Nested("respell", IntentShape.Of(
            Field.Text("from", required: true),
            Field.Text("to", required: true)), many: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_phoneme");

    /// <inheritdoc />
    public override EditPhonemeIntent Parse(JsonElement authored) => EditPhonemeIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditPhonemeIntent intent) =>
        EditPhonemeComposer.Build(context.Cache, intent, context.MintId);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, EditPhonemeIntent intent) =>
        EditPhonemeComposer.ReadAffectedUsers(cache, intent);
}
