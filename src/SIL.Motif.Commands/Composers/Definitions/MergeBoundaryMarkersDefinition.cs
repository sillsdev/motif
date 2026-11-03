using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Merges a stray duplicate of the standard boundary marker into it, re-pointing its uses.</summary>
internal sealed class MergeBoundaryMarkersDefinition : ComposerDefinition<MergeBoundaryMarkersIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-merge-boundary-markers";

    /// <inheritdoc />
    public override string Composer => "MergeBoundaryMarkers";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("marker", IdRole.Target, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_merge_boundary_markers", AgentTool: false);

    /// <inheritdoc />
    public override MergeBoundaryMarkersIntent Parse(JsonElement authored) => MergeBoundaryMarkersIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MergeBoundaryMarkersIntent intent) =>
        MergeBoundaryMarkersComposer.Build(context.Cache, intent);
}
