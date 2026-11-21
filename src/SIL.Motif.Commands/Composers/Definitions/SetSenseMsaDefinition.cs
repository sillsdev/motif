using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Points an existing sense at another analysis of its entry, keeping the sense and its content.</summary>
internal sealed class SetSenseMsaDefinition : ComposerDefinition<SetSenseMsaIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-sense-msa";

    /// <inheritdoc />
    public override string Composer => "SetSenseMsa";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("sense", IdRole.Target, required: true),
        Field.Id("analysis", IdRole.Reference, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_sense_msa");

    /// <inheritdoc />
    public override SetSenseMsaIntent Parse(JsonElement authored) => SetSenseMsaIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetSenseMsaIntent intent) =>
        SetSenseMsaComposer.Build(context.Cache, intent, context.MintId);
}
