using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages one new circumfix entry: a prefix part and a suffix part that attach together.</summary>
internal sealed class AuthorCircumfixDefinition : ComposerDefinition<AuthorCircumfixIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-circumfix";

    /// <inheritdoc />
    public override string Composer => "AuthorCircumfix";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses", "infixation"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("prefix", required: true),
        Field.Text("suffix", required: true),
        Field.Text("formWs", required: true),
        Field.Text("gloss", required: true),
        Field.Text("analysisWs", required: true),
        Field.Text("definition"),
        Field.Nested("msa", IntentShape.Of(
            Field.Choice("kind", ["inflectional", "derivational", "unclassified"], required: true),
            Field.Id("category", IdRole.Reference, required: true),
            Field.Id("toCategory", IdRole.Reference),
            Field.Ids("slots", IdRole.Reference)), required: true),
        Field.Ids("prefixEnvironments", IdRole.Reference),
        Field.Ids("suffixEnvironments", IdRole.Reference),
        Field.Ids("inflectionClasses", IdRole.Reference));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_circumfix");

    /// <inheritdoc />
    public override AuthorCircumfixIntent Parse(JsonElement authored) => AuthorCircumfixIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorCircumfixIntent intent) =>
        AuthorCircumfixComposer.Build(context.Cache, intent, context.MintId);
}
