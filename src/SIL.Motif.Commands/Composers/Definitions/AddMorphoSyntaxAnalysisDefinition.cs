using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages a second grammatical info on an existing entry, so one entry serves two categories or two host classes.</summary>
internal sealed class AddMorphoSyntaxAnalysisDefinition : ComposerDefinition<AddMorphoSyntaxAnalysisIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-add-morphosyntax-analysis";

    /// <inheritdoc />
    public override string Composer => "AddMorphoSyntaxAnalysis";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses", "inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Target, required: true),
        Field.Nested("msa", IntentShape.Of(
            Field.Choice("kind", ["stem", "inflectional", "derivational", "unclassified"], required: true),
            Field.Id("category", IdRole.Reference, required: true),
            Field.Id("toCategory", IdRole.Reference),
            Field.Ids("slots", IdRole.Reference),
            Field.Id("inflectionClass", IdRole.Reference),
            Field.Ids("exceptionFeatures", IdRole.Reference)), required: true),
        Field.Ids("senses", IdRole.Reference),
        Field.Text("gloss"),
        Field.Text("analysisWs"),
        Field.Text("definition"),
        Field.Id("fromInflectionClass", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_morphosyntax_analysis");

    /// <inheritdoc />
    public override AddMorphoSyntaxAnalysisIntent Parse(JsonElement authored) =>
        AddMorphoSyntaxAnalysisIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AddMorphoSyntaxAnalysisIntent intent) =>
        AddMorphoSyntaxAnalysisComposer.Build(context.Cache, intent, context.MintId);
}
