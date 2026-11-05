using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Adds Required Features to an existing or earlier-staged affix allomorph.</summary>
internal sealed class AuthorAllomorphRequiredFeaturesDefinition : ComposerDefinition<AuthorAllomorphRequiredFeaturesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-allomorph-required-features";

    /// <inheritdoc />
    public override string Composer => "AuthorAllomorphRequiredFeatures";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-features"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("allomorph", IdRole.Target, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_allomorph_required_features");

    /// <inheritdoc />
    public override AuthorAllomorphRequiredFeaturesIntent Parse(JsonElement authored) => AuthorAllomorphRequiredFeaturesIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorAllomorphRequiredFeaturesIntent intent) =>
        AuthorAllomorphRequiredFeaturesComposer.Build(context.Cache, intent, context.MintId);
}
