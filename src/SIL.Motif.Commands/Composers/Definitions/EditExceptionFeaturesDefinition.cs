using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Sets the exception features a stem carries, or the ones an inflectional affix requires of its stem.</summary>
internal sealed class EditExceptionFeaturesDefinition : ComposerDefinition<EditExceptionFeaturesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-exception-features";

    /// <inheritdoc />
    public override string Composer => "EditExceptionFeatures";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("msa", IdRole.Target, required: true),
        Field.Ids("exceptionFeatures", IdRole.Reference, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_exception_features");

    /// <inheritdoc />
    public override EditExceptionFeaturesIntent Parse(JsonElement authored) =>
        EditExceptionFeaturesIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditExceptionFeaturesIntent intent) =>
        EditExceptionFeaturesComposer.Build(context.Cache, intent);
}
