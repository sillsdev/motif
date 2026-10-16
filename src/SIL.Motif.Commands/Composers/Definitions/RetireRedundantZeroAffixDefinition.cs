using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;

namespace SIL.Motif.Commands;

/// <summary>Retires one whole zero-affix entry that is redundant and unused.</summary>
internal sealed class RetireRedundantZeroAffixDefinition : ComposerDefinition<RetireRedundantZeroAffixIntentDocument>
{
    /// <inheritdoc />
    public override string Verb => "retire-redundant-zero-affix";

    /// <inheritdoc />
    public override string Composer => "RetireRedundantZeroAffix";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } =
        ["affix-slots-and-templates", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Choice("format", ["motif-retire-redundant-zero-affix"], required: true),
        Field.Number("version", required: true),
        Field.Nested("retirement", IntentShape.Of(Field.Id("entry", IdRole.Reference, required: true)),
            required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = new(CommandSurface.AdvancedAi, AgentClass.Draft,
        OnByDefault: false, InBatch: false, ToolName: "motif_retire_redundant_zero_affix", HandWrittenTool: true);

    /// <inheritdoc />
    public override RetireRedundantZeroAffixIntentDocument Parse(JsonElement authored) =>
        RetireRedundantZeroAffixCodec.Parse(authored.GetRawText());

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(
        ComposeContext context, RetireRedundantZeroAffixIntentDocument intent) =>
        RetireRedundantZeroAffixComposer.Build(context.Cache, intent, context.MintId);
}
