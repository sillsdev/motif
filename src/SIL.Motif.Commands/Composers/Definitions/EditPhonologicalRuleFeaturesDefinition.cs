using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Limits a phonological rule to words that carry, or lack, given exception features or inflection classes.</summary>
internal sealed class EditPhonologicalRuleFeaturesDefinition : ComposerDefinition<EditPhonologicalRuleFeaturesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-phonological-rule-features";

    /// <inheritdoc />
    public override string Composer => "EditPhonologicalRuleFeatures";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("rule", IdRole.Target, required: true),
        Field.Ids("requiredFeatures", IdRole.Reference),
        Field.Ids("excludedFeatures", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_phonological_rule_features");

    /// <inheritdoc />
    public override EditPhonologicalRuleFeaturesIntent Parse(JsonElement authored) =>
        EditPhonologicalRuleFeaturesIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditPhonologicalRuleFeaturesIntent intent) =>
        EditPhonologicalRuleFeaturesComposer.Build(context.Cache, intent, context.MintId);
}
