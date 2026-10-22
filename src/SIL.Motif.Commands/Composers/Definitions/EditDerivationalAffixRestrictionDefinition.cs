using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Restricts the stems a derivational affix attaches to by inflection class and exception features.</summary>
internal sealed class EditDerivationalAffixRestrictionDefinition : ComposerDefinition<EditDerivationalAffixRestrictionIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-derivational-affix-restriction";

    /// <inheritdoc />
    public override string Composer => "EditDerivationalAffixRestriction";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("msa", IdRole.Target, required: true),
        Field.Id("fromInflectionClass", IdRole.Reference),
        Field.Flag("clearFromInflectionClass", trueOnly: true),
        Field.Ids("fromExceptionFeatures", IdRole.Reference))
        .AtMostOne("fromInflectionClass", "clearFromInflectionClass");

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_edit_derivational_affix_restriction");

    /// <inheritdoc />
    public override EditDerivationalAffixRestrictionIntent Parse(JsonElement authored) =>
        EditDerivationalAffixRestrictionIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditDerivationalAffixRestrictionIntent intent) =>
        EditDerivationalAffixRestrictionComposer.Build(context.Cache, intent);
}
