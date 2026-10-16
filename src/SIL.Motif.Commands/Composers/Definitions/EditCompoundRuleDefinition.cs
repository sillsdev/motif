using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Turns one compound rule off or on, so a rule that builds non-words can be switched out of the parse.</summary>
internal sealed class EditCompoundRuleDefinition : ComposerDefinition<EditCompoundRuleIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-compound-rule";

    /// <inheritdoc />
    public override string Composer => "EditCompoundRule";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["compounding"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Flag("expectedDisabled", required: true),
        Field.Flag("disabled", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, AgentTool: false);

    /// <inheritdoc />
    public override EditCompoundRuleIntent Parse(JsonElement authored) => EditCompoundRuleIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditCompoundRuleIntent intent) =>
        EditCompoundRuleComposer.Build(context.Cache, intent);
}
