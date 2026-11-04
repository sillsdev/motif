using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Retires an inflectional affix slot that no affix and no template uses.</summary>
internal sealed class RetireAffixSlotDefinition : ComposerDefinition<RetireAffixSlotIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-retire-affix-slot";

    /// <inheritdoc />
    public override string Composer => "RetireAffixSlot";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["affix-slots-and-templates"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("slot", IdRole.Target, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_retire_affix_slot", AgentTool: false);

    /// <inheritdoc />
    public override RetireAffixSlotIntent Parse(JsonElement authored) => RetireAffixSlotIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RetireAffixSlotIntent intent) =>
        RetireAffixSlotComposer.Build(context.Cache, intent);
}
