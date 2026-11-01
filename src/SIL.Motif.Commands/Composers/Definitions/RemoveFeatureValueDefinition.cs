using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Removes a feature specification from a stem's or an inflectional affix's feature structure.</summary>
internal sealed class RemoveFeatureValueDefinition : ComposerDefinition<RemoveFeatureValueIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-remove-feature-value";

    /// <inheritdoc />
    public override string Composer => "RemoveFeatureValue";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-features"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("featStruc", IdRole.Reference, required: true),
        Field.Id("feature", IdRole.Reference, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_remove_feature_value");

    /// <inheritdoc />
    public override RemoveFeatureValueIntent Parse(JsonElement authored) => RemoveFeatureValueIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RemoveFeatureValueIntent intent) =>
        RemoveFeatureValueComposer.Build(context.Cache, intent, context.MintId);
}
