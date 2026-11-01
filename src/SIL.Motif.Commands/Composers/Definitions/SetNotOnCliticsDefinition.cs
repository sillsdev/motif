using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Chooses whether the parser's phonological rules also run on a word with its clitic attached.</summary>
internal sealed class SetNotOnCliticsDefinition : ComposerDefinition<SetNotOnCliticsIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-not-on-clitics";

    /// <inheritdoc />
    public override string Composer => "SetNotOnClitics";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Flag("expectedNotOnClitics", required: true),
        Field.Flag("notOnClitics", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_not_on_clitics");

    /// <inheritdoc />
    public override SetNotOnCliticsIntent Parse(JsonElement authored) =>
        SetNotOnCliticsIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetNotOnCliticsIntent intent) =>
        SetNotOnCliticsComposer.Build(context.Cache, intent);
}
