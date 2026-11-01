using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Adds an exception feature to the project's Exception Features list.</summary>
internal sealed class AuthorExceptionFeatureDefinition : ComposerDefinition<AuthorExceptionFeatureIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-exception-feature";

    /// <inheritdoc />
    public override string Composer => "AuthorExceptionFeature";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("name", required: true),
        Field.Text("ws", required: true),
        Field.Text("abbreviation"));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_author_exception_feature");

    /// <inheritdoc />
    public override AuthorExceptionFeatureIntent Parse(JsonElement authored) =>
        AuthorExceptionFeatureIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorExceptionFeatureIntent intent) =>
        AuthorExceptionFeatureComposer.Build(context.Cache, intent, context.MintId);
}
