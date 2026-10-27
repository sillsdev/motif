using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Stages one new affix whose form is a stem pattern and a copy-and-insert realization.</summary>
internal sealed class AuthorAffixProcessDefinition : ComposerDefinition<AuthorAffixProcessIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-affix-process";

    /// <inheritdoc />
    public override string Composer => "AuthorAffixProcess";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["infixation", "reduplication"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("morphType", IdRole.Reference, required: true),
        Field.Text("gloss", required: true),
        Field.Text("analysisWs", required: true),
        Field.Text("definition"),
        Field.Text("form"),
        Field.Text("formWs"),
        Field.Nested("msa", IntentShape.Of(
            Field.Choice("kind", ["inflectional", "derivational", "unclassified"], required: true),
            Field.Id("category", IdRole.Reference, required: true),
            Field.Id("toCategory", IdRole.Reference),
            Field.Ids("slots", IdRole.Reference)), required: true),
        Field.Nested("input", IntentShape.Of(
            Field.Text("name", required: true),
            Field.Id("phoneme", IdRole.Reference),
            Field.Id("naturalClass", IdRole.Reference),
            Field.Flag("remaining")), required: true, many: true),
        Field.Nested("output", IntentShape.Of(
            Field.Text("copy"),
            Field.Ids("insert", IdRole.Reference)), required: true, many: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_affix_process");

    /// <inheritdoc />
    public override AuthorAffixProcessIntent Parse(JsonElement authored) => AuthorAffixProcessIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorAffixProcessIntent intent) =>
        AuthorAffixProcessComposer.Build(context.Cache, intent, context.MintId);
}
