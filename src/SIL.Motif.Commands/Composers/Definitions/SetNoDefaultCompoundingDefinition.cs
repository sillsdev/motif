using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Turns the parser's built-in default compounding off or on for the project.</summary>
internal sealed class SetNoDefaultCompoundingDefinition : ComposerDefinition<SetNoDefaultCompoundingIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-no-default-compounding";

    /// <inheritdoc />
    public override string Composer => "SetNoDefaultCompounding";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["compounding"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Flag("expectedNoDefaultCompounding", required: true),
        Field.Flag("noDefaultCompounding", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_no_default_compounding");

    /// <inheritdoc />
    public override SetNoDefaultCompoundingIntent Parse(JsonElement authored) =>
        SetNoDefaultCompoundingIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetNoDefaultCompoundingIntent intent) =>
        SetNoDefaultCompoundingComposer.Build(context.Cache, intent);
}
