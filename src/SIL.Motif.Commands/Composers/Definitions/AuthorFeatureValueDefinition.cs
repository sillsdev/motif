using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Adds a feature specification to an existing or earlier-staged feature structure.</summary>
internal sealed class AuthorFeatureValueDefinition : ComposerDefinition<AuthorFeatureValueIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-feature-value";

    /// <inheritdoc />
    public override string Composer => "AuthorFeatureValue";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-features"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("featStruc", IdRole.Reference, required: true),
        Field.Id("feature", IdRole.Reference, required: true),
        Field.Id("value", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, OnByDefault: true, ToolName: "motif_add_feature_value");

    /// <inheritdoc />
    public override AuthorFeatureValueIntent Parse(JsonElement authored) => AuthorFeatureValueIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorFeatureValueIntent intent) =>
        AuthorFeatureValueComposer.Build(context.Cache, intent);
}
