using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>The intent shapes the rule composers share.</summary>
internal static class PhonologicalRuleShapes
{
    private static readonly string[] Directions = ["left-to-right", "right-to-left", "simultaneous"];

    public static IntentShape Item { get; } = IntentShape.Of(
        Field.Id("phoneme", IdRole.Reference),
        Field.Id("naturalClass", IdRole.Reference));

    public static IntentShape Context { get; } = IntentShape.Of(
        Field.Id("phoneme", IdRole.Reference),
        Field.Id("naturalClass", IdRole.Reference),
        Field.Choice("boundary", ["word", "morpheme"]),
        Field.Id("boundaryMarker", IdRole.Reference),
        Field.Flag("optional", trueOnly: true),
        Field.Number("minimum"),
        Field.Number("maximum"));

    public static IntentField[] Parts() =>
    [
        Field.Text("name"),
        Field.Choice("direction", Directions),
        Field.Nested("input", Item, many: true),
        Field.Nested("output", Item, many: true),
        Field.Nested("left", Context, many: true),
        Field.Nested("right", Context, many: true),
    ];
}

/// <summary>Replaces parts of an existing rule in place, keeping its identity, position and rule features.</summary>
internal sealed class EditPhonologicalRuleDefinition : ComposerDefinition<EditPhonologicalRuleIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-phonological-rule";

    /// <inheritdoc />
    public override string Composer => "EditPhonologicalRule";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        [Field.Id("rule", IdRole.Target, required: true), .. PhonologicalRuleShapes.Parts(),
         Field.Nested("expected", IntentShape.Of(PhonologicalRuleShapes.Parts()))]);

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_phonological_rule");

    /// <inheritdoc />
    public override EditPhonologicalRuleIntent Parse(JsonElement authored) =>
        EditPhonologicalRuleIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditPhonologicalRuleIntent intent) =>
        EditPhonologicalRuleComposer.Build(context.Cache, intent, context.MintId);
}

/// <summary>Moves rules within the ordered rule list.</summary>
internal sealed class OrderPhonologicalRulesDefinition : ComposerDefinition<OrderPhonologicalRulesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-order-phonological-rules";

    /// <inheritdoc />
    public override string Composer => "OrderPhonologicalRules";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("rule", IdRole.Target),
        Field.Id("after", IdRole.Reference),
        Field.Id("before", IdRole.Reference),
        Field.Ids("order", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_order_phonological_rules");

    /// <inheritdoc />
    public override OrderPhonologicalRulesIntent Parse(JsonElement authored) =>
        OrderPhonologicalRulesIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, OrderPhonologicalRulesIntent intent) =>
        OrderPhonologicalRulesComposer.Build(context.Cache, intent, context.MintId);
}

/// <summary>Turns one phonological rule off or on.</summary>
internal sealed class SetPhonologicalRuleDisabledDefinition : ComposerDefinition<SetPhonologicalRuleDisabledIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-phonological-rule-disabled";

    /// <inheritdoc />
    public override string Composer => "SetPhonologicalRuleDisabled";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Flag("expectedDisabled", required: true),
        Field.Flag("disabled", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_phonological_rule_disabled");

    /// <inheritdoc />
    public override SetPhonologicalRuleDisabledIntent Parse(JsonElement authored) =>
        SetPhonologicalRuleDisabledIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetPhonologicalRuleDisabledIntent intent) =>
        SetPhonologicalRuleDisabledComposer.Build(context.Cache, intent);
}

/// <summary>Deletes one phonological rule that nothing else uses.</summary>
internal sealed class DeletePhonologicalRuleDefinition : ComposerDefinition<DeletePhonologicalRuleIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-phonological-rule";

    /// <inheritdoc />
    public override string Composer => "DeletePhonologicalRule";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["phonological-rules"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Text("expectedName", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_phonological_rule");

    /// <inheritdoc />
    public override DeletePhonologicalRuleIntent Parse(JsonElement authored) =>
        DeletePhonologicalRuleIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeletePhonologicalRuleIntent intent) =>
        DeletePhonologicalRuleComposer.Build(context.Cache, intent);
}
