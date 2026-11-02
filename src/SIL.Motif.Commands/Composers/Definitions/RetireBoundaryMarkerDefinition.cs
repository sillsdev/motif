using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Retires a stray duplicate of the standard boundary marker that nothing uses.</summary>
internal sealed class RetireBoundaryMarkerDefinition : ComposerDefinition<RetireBoundaryMarkerIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-retire-boundary-marker";

    /// <inheritdoc />
    public override string Composer => "RetireBoundaryMarker";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("marker", IdRole.Target, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_retire_boundary_marker", AgentTool: false);

    /// <inheritdoc />
    public override RetireBoundaryMarkerIntent Parse(JsonElement authored) => RetireBoundaryMarkerIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RetireBoundaryMarkerIntent intent) =>
        RetireBoundaryMarkerComposer.Build(context.Cache, intent);
}
