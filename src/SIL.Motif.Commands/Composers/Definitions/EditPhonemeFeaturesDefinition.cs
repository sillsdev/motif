using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Sets or clears phonological feature values on one existing phoneme.</summary>
internal sealed class EditPhonemeFeaturesDefinition : ComposerDefinition<EditFeatureValuesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-phoneme-features";

    /// <inheritdoc />
    public override string Composer => "EditPhonemeFeatures";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Nested("set", IntentShape.Of(
            Field.Id("feature", IdRole.Reference, required: true),
            Field.Id("value", IdRole.Reference, required: true)), many: true),
        Field.Ids("clear", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_phoneme_features");

    /// <inheritdoc />
    public override EditFeatureValuesIntent Parse(JsonElement authored) => EditFeatureValuesIntentParser.Parse(authored, "EditPhonemeFeatures");

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditFeatureValuesIntent intent) =>
        EditPhonemeFeaturesComposer.Build(context.Cache, intent, context.MintId);
}
