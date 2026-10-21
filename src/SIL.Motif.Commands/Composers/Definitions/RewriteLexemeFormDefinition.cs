using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Rewrites the lexeme forms of existing entries, optionally keeping the old spelling as the citation form.</summary>
internal sealed class RewriteLexemeFormDefinition : ComposerDefinition<RewriteLexemeFormIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-rewrite-lexeme-form";

    /// <inheritdoc />
    public override string Composer => "RewriteLexemeForm";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Nested("entries", IntentShape.Of(
            Field.Id("entryGuid", IdRole.Target, required: true),
            Field.Text("writingSystem", required: true),
            Field.Text("expectedForm", required: true),
            Field.Text("newForm")), required: true, many: true),
        Field.Flag("setEmptyCitationForm"),
        Field.Flag("foldStressMarks"));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_rewrite_lexeme_form");

    /// <inheritdoc />
    public override RewriteLexemeFormIntent Parse(JsonElement authored) => RewriteLexemeFormIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RewriteLexemeFormIntent intent) =>
        RewriteLexemeFormComposer.Build(context.Cache, intent);
}
