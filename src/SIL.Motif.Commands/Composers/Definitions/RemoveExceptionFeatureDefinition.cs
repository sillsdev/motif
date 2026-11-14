using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Takes an exception feature off every stem and phonological rule branch that carries it.</summary>
internal sealed class RemoveExceptionFeatureDefinition : ComposerDefinition<RemoveExceptionFeatureIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-remove-exception-feature";

    /// <inheritdoc />
    public override string Composer => "RemoveExceptionFeature";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("feature", required: true),
        Field.Text("expectedName", required: true),
        Field.Flag("keepFeature"));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_remove_exception_feature", AgentTool: false);

    /// <inheritdoc />
    public override RemoveExceptionFeatureIntent Parse(JsonElement authored) => RemoveExceptionFeatureIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RemoveExceptionFeatureIntent intent) =>
        RemoveExceptionFeatureComposer.Build(context.Cache, intent);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, RemoveExceptionFeatureIntent intent) =>
        RemoveExceptionFeatureComposer.Summarize(cache, intent);
}
