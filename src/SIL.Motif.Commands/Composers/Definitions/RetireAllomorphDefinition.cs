using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Retirement;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;

namespace SIL.Motif.Commands;

/// <summary>
/// The one allomorph retirement a Draft is authored through, whether the standalone duplicate-form document or the
/// sound-rule replacement document; the authored <c>format</c> decides which.
/// </summary>
internal sealed class RetireAllomorphDefinition : ComposerDefinition<RetireAllomorphDefinition.Authored>
{
    /// <summary>
    /// The parsed document: exactly one of the two, since a shape cannot say which fields belong with which format.
    /// </summary>
    /// <param name="Standalone">The duplicate-form retirement, or null for a rule replacement.</param>
    /// <param name="RuleReplacement">The sound-rule replacement, or null for a duplicate-form retirement.</param>
    internal sealed record Authored(
        RetireAllomorphIntentDocument? Standalone, ReplaceListedAllomorphsWithRuleIntent? RuleReplacement);

    /// <inheritdoc />
    public override string Verb => "retire-allomorph";

    /// <inheritdoc />
    public override string Composer => RetirementRecipes.RuleReplacement.ComposerName;

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["conditioned-allomorphy", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    /// <remarks>
    /// The union of the two documents. Only <c>format</c> and <c>version</c> are required here; the parser requires
    /// <c>retirement</c> and <c>duplicateSurvivors</c> for the standalone format, and <c>naturalClass</c>,
    /// <c>rule</c>, <c>retirements</c> and <c>operations</c> for the rule replacement.
    /// </remarks>
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Choice("format", ["motif-retire-allomorph", RetirementRecipes.RuleReplacement.ProvenanceFormat],
            required: true),
        Field.Number("version", required: true),
        Field.Nested("retirement", RetirementShapes.Retirement),
        Field.Nested("duplicateSurvivors", RetirementShapes.AllomorphIdentity, many: true),
        Field.Nested("naturalClass", RetirementShapes.NaturalClass),
        Field.Nested("rule", RetirementShapes.Rule),
        Field.Nested("retirements", RetirementShapes.Retirement, many: true),
        Field.Nested("operations", RetirementShapes.OperationBinding, many: true),
        Field.Nested("display", RetirementShapes.Display));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = new(CommandSurface.AdvancedAi, AgentClass.Draft,
        InBatch: false, ToolName: "motif_retire_allomorph", HandWrittenTool: true);

    /// <inheritdoc />
    public override Authored Parse(JsonElement authored)
    {
        var json = authored.GetRawText();
        return authored.TryGetProperty("format", out var format) &&
               format.ValueKind == JsonValueKind.String &&
               format.GetString() == RetirementRecipes.RuleReplacement.ProvenanceFormat
            ? new Authored(null, AllomorphRetirementCodec.Parse(json))
            : new Authored(RetireAllomorphCodec.Parse(json), null);
    }

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, Authored intent) =>
        intent.RuleReplacement is { } replacement
            ? ReplaceListedAllomorphsWithRuleComposer.Build(context.Cache, replacement, context.MintId)
            : RetireAllomorphComposer.Build(context.Cache, intent.Standalone!, context.MintId);
}
