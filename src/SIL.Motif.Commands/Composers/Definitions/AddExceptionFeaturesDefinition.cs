using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Adds exception features to many stems, inflectional affixes or derivational affixes at once, keeping the ones they already have.</summary>
internal sealed class AddExceptionFeaturesDefinition : ComposerDefinition<AddExceptionFeaturesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-add-exception-features";

    /// <inheritdoc />
    public override string Composer => "AddExceptionFeatures";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Ids("msas", IdRole.Target, required: true),
        Field.Ids("exceptionFeatures", IdRole.Reference, required: true),
        Field.Choice("side", ["from", "to"]));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_exception_features", AgentTool: false);

    /// <inheritdoc />
    public override AddExceptionFeaturesIntent Parse(JsonElement authored) => AddExceptionFeaturesIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AddExceptionFeaturesIntent intent) =>
        AddExceptionFeaturesComposer.Build(context.Cache, intent);
}
