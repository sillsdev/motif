using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>Changes an existing affix between inflectional, derivational and unclassified in place.</summary>
internal sealed class ChangeAffixKindDefinition : ComposerDefinition<ChangeAffixKindIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-change-affix-kind";

    /// <inheritdoc />
    public override string Composer => "ChangeAffixKind";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["affix-slots-and-templates", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("msa", IdRole.Target, required: true),
        Field.Choice("kind", ["inflectional", "derivational", "unclassified"], required: true),
        Field.Id("toCategory", IdRole.Reference),
        Field.Ids("slots", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_change_affix_kind");

    /// <inheritdoc />
    public override ChangeAffixKindIntent Parse(JsonElement authored) => ChangeAffixKindIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, ChangeAffixKindIntent intent) =>
        ChangeAffixKindComposer.Build(context.Cache, intent, context.MintId);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, ChangeAffixKindIntent intent) =>
        [.. ChangeAffixKindComposer.ReadDirection(cache, intent), .. ChangeAffixKindComposer.ReadWarnings(cache, intent)];
}
