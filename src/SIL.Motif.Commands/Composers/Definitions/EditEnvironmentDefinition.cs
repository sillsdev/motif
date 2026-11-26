using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>
/// Rewrites the left or right context of one existing phonological environment in place, so the allomorphs that
/// name it keep their link.
/// </summary>
internal sealed class EditEnvironmentDefinition : ComposerDefinition<EditEnvironmentIntent>
{
    private static readonly IntentShape Context = IntentShape.Of(
        Field.Id("phoneme", IdRole.Reference),
        Field.Id("naturalClass", IdRole.Reference),
        Field.Text("boundary"),
        Field.Id("boundaryMarker", IdRole.Reference));

    /// <inheritdoc />
    public override string Verb => "compose-edit-environment";

    /// <inheritdoc />
    public override string Composer => "EditEnvironment";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory", "conditioned-allomorphy"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("environment", IdRole.Target, required: true),
        Field.Text("expectedText", required: true),
        Field.Nested("left", Context, many: true),
        Field.Nested("right", Context, many: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_environment", AgentTool: false);

    /// <inheritdoc />
    public override EditEnvironmentIntent Parse(JsonElement authored) => EditEnvironmentIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditEnvironmentIntent intent) =>
        EditEnvironmentComposer.Build(context.Cache, intent, context.MintId);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, EditEnvironmentIntent intent) =>
        EditEnvironmentComposer.ReadAffectedUsers(cache, intent);
}
