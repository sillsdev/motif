using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Sets how many times one affix may apply to a word, for a stacked extension or a repeated suffix.</summary>
internal sealed class SetAffixMaxAppsDefinition : ComposerDefinition<SetAffixMaxAppsIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-affix-max-apps";

    /// <inheritdoc />
    public override string Composer => "SetAffixMaxApps";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["affix-slots-and-templates", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("msa", IdRole.Target, required: true),
        Field.Number("expectedMaxApps", required: true),
        Field.Number("maxApps", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_affix_max_apps");

    /// <inheritdoc />
    public override SetAffixMaxAppsIntent Parse(JsonElement authored) =>
        SetAffixMaxAppsIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, SetAffixMaxAppsIntent intent) =>
        SetAffixMaxAppsReport.Related(cache, intent) is { Count: > 0 } related ? related : null;

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetAffixMaxAppsIntent intent) =>
        SetAffixMaxAppsComposer.Build(context.Cache, intent);
}
