using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Deletes one whole entry that nothing outside it refers to.</summary>
internal sealed class DeleteLexicalEntryDefinition : ComposerDefinition<DeleteLexicalEntryIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-lexical-entry";

    /// <inheritdoc />
    public override string Composer => "DeleteLexicalEntry";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(Field.Id("entry", IdRole.Target, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_lexical_entry", AgentTool: false);

    /// <inheritdoc />
    public override DeleteLexicalEntryIntent Parse(JsonElement authored) => DeleteLexicalEntryIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteLexicalEntryIntent intent) =>
        DeleteLexicalEntryComposer.Build(context.Cache, intent);
}
