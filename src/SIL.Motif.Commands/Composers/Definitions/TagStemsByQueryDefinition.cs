using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Tags every stem a query selects with one exception feature, choosing the polarity that tags the fewest stems FieldWorks can express.</summary>
internal sealed class TagStemsByQueryDefinition : ComposerDefinition<TagStemsByQueryIntent>
{
    private static readonly IntentShape QueryShape = IntentShape.Of(
        Field.Id("category"), Field.Text("finalLetters"), Field.Id("hasFeature"), Field.Id("lacksFeature"),
        Field.Text("glossContains"));

    /// <inheritdoc />
    public override string Verb => "compose-tag-stems-by-query";

    /// <inheritdoc />
    public override string Composer => "TagStemsByQuery";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Nested("query", QueryShape, required: true),
        Field.Id("exceptionFeature", required: true),
        Field.Choice("gate", ["rule", "affix"]),
        Field.Choice("action", ["tag", "untag"]));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_tag_stems_by_query", AgentTool: false);

    /// <inheritdoc />
    public override TagStemsByQueryIntent Parse(JsonElement authored) => TagStemsByQueryIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, TagStemsByQueryIntent intent) =>
        TagStemsByQueryComposer.Build(context.Cache, intent);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, TagStemsByQueryIntent intent) =>
        TagStemsByQueryComposer.Summarize(cache, intent);
}
