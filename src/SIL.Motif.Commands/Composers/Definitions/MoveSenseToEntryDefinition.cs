using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Moves a sense, with its sub-senses and examples, out of its entry into a new entry of its own.</summary>
internal sealed class MoveSenseToEntryDefinition : ComposerDefinition<MoveSenseToEntryIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-move-sense-to-entry";

    /// <inheritdoc />
    public override string Composer => "MoveSenseToEntry";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["senses-and-definitions", "lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("sense", IdRole.Target, required: true),
        Field.Text("form", required: true),
        Field.Text("formWs", required: true),
        Field.Id("morphType", IdRole.Reference, required: true),
        Field.Nested("msa", IntentShape.Of(
            Field.Choice("kind", ["stem", "inflectional", "derivational", "unclassified"], required: true),
            Field.Id("category", IdRole.Reference, required: true),
            Field.Id("toCategory", IdRole.Reference),
            Field.Ids("slots", IdRole.Reference),
            Field.Id("inflectionClass", IdRole.Reference))));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_move_sense_to_entry", AgentTool: false);

    /// <inheritdoc />
    public override MoveSenseToEntryIntent Parse(JsonElement authored) => MoveSenseToEntryIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MoveSenseToEntryIntent intent) =>
        MoveSenseToEntryComposer.Build(context.Cache, intent, context.MintId);
}
