using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Adds one value to an existing closed phonological feature.</summary>
internal sealed class AddPhonologicalFeatureValueDefinition : ComposerDefinition<AddPhonologicalFeatureValueIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-add-phonological-feature-value";

    /// <inheritdoc />
    public override string Composer => "AddPhonologicalFeatureValue";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory", "natural-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("feature", IdRole.Reference, required: true),
        Field.Text("name", required: true),
        Field.Text("abbreviation"));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_phonological_feature_value");

    /// <inheritdoc />
    public override AddPhonologicalFeatureValueIntent Parse(JsonElement authored) => AddPhonologicalFeatureValueIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AddPhonologicalFeatureValueIntent intent) =>
        AddPhonologicalFeatureValueComposer.Build(context.Cache, intent, context.MintId);
}
