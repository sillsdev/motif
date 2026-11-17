using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages the Attribution notice in the project's Description.</summary>
internal sealed class SetProjectAttributionDefinition : ComposerDefinition<SetProjectAttributionIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-project-attribution";
    /// <inheritdoc />
    public override string Composer => "SetProjectAttribution";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("tag", required: true),
        Field.Text("analysisWs"));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.Released, AgentClass.Draft, ToolName: "motif_set_project_attribution");
    /// <inheritdoc />
    public override SetProjectAttributionIntent Parse(JsonElement authored) => SetProjectAttributionIntentParser.Parse(authored);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetProjectAttributionIntent intent) =>
        SetProjectAttributionComposer.Build(context.Cache, intent, context.MintId);
}
