using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Merges one entry into another, so the two become one entry.</summary>
internal sealed class MergeLexicalEntriesDefinition : ComposerDefinition<MergeLexicalEntriesIntent>
{
    public override string Verb => "compose-merge-lexical-entries";
    public override string Composer => "MergeLexicalEntries";
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Target, required: true),
        Field.Id("into", IdRole.Reference, required: true));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_merge_lexical_entries");
    public override MergeLexicalEntriesIntent Parse(JsonElement authored) => MergeLexicalEntriesIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MergeLexicalEntriesIntent intent) =>
        MergeLexicalEntriesComposer.Build(context.Cache, intent);
}
