using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Defines a closed phonological feature and its values, such as place with labial, coronal and velar.</summary>
internal sealed class AuthorPhonologicalFeatureDefinition : ComposerDefinition<AuthorPhonologicalFeatureIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-phonological-feature";

    /// <inheritdoc />
    public override string Composer => "AuthorPhonologicalFeature";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory", "natural-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("name", required: true),
        Field.Text("abbreviation"),
        Field.Nested("values", IntentShape.Of(
            Field.Text("name", required: true),
            Field.Text("abbreviation")), required: true, many: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_phonological_feature");

    /// <inheritdoc />
    public override AuthorPhonologicalFeatureIntent Parse(JsonElement authored) => AuthorPhonologicalFeatureIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorPhonologicalFeatureIntent intent) =>
        AuthorPhonologicalFeatureComposer.Build(context.Cache, intent, context.MintId);
}
