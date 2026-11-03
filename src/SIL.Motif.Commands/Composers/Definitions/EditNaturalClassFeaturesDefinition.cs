using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Sets or clears the feature constraints of one feature-defined natural class.</summary>
internal sealed class EditNaturalClassFeaturesDefinition : ComposerDefinition<EditFeatureValuesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-natural-class-features";

    /// <inheritdoc />
    public override string Composer => "EditNaturalClassFeatures";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["natural-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Nested("set", IntentShape.Of(
            Field.Id("feature", IdRole.Reference, required: true),
            Field.Id("value", IdRole.Reference, required: true)), many: true),
        Field.Ids("clear", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_natural_class_features");

    /// <inheritdoc />
    public override EditFeatureValuesIntent Parse(JsonElement authored) => EditFeatureValuesIntentParser.Parse(authored, "EditNaturalClassFeatures");

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditFeatureValuesIntent intent) =>
        EditNaturalClassFeaturesComposer.Build(context.Cache, intent, context.MintId);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, EditFeatureValuesIntent intent) =>
        EditNaturalClassComposer.ReadAffectedUsers(cache, intent.Target);
}
