using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages the Attribution tag on many lexical entries' Bibliography.</summary>
internal sealed class SetEntryAttributionDefinition : ComposerDefinition<SetEntryAttributionIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-entry-attribution";
    /// <inheritdoc />
    public override string Composer => "SetEntryAttribution";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("tag", required: true),
        Field.Text("analysisWs"),
        Field.Nested("entries", IntentShape.Of(
            Field.Id("entry", IdRole.Target, required: true),
            Field.Text("at")), required: true, many: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.Released, AgentClass.Draft, ToolName: "motif_set_entry_attribution");
    /// <inheritdoc />
    public override SetEntryAttributionIntent Parse(JsonElement authored) => SetEntryAttributionIntentParser.Parse(authored);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetEntryAttributionIntent intent) =>
        SetEntryAttributionComposer.Build(context.Cache, intent, context.MintId);
}
