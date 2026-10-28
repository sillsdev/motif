using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages an entry's FieldWorks do-not-parse flag.</summary>
internal sealed class SetEntryDoNotParseDefinition : ComposerDefinition<SetEntryDoNotParseIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-entry-do-not-parse";
    /// <inheritdoc />
    public override string Composer => "SetEntryDoNotParse";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Flag("expectedDoNotParse", required: true),
        Field.Flag("doNotParse", required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_entry_do_not_parse");
    /// <inheritdoc />
    public override SetEntryDoNotParseIntent Parse(JsonElement authored) => SetEntryDoNotParseIntentParser.Parse(authored);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetEntryDoNotParseIntent intent) =>
        SetEntryDoNotParseComposer.Build(context.Cache, intent);
}
