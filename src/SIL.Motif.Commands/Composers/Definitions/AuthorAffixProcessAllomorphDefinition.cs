using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Stages one more pattern-based form for an existing affix entry that already realizes itself as a process.</summary>
internal sealed class AuthorAffixProcessAllomorphDefinition : ComposerDefinition<AuthorAffixProcessAllomorphIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-author-affix-process-allomorph";

    /// <inheritdoc />
    public override string Composer => "AuthorAffixProcessAllomorph";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["infixation", "conditioned-allomorphy", "stem-alternation"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Reference, required: true),
        Field.Nested("input", IntentShape.Of(
            Field.Text("name", required: true),
            Field.Id("phoneme", IdRole.Reference),
            Field.Id("naturalClass", IdRole.Reference),
            Field.Flag("remaining")), required: true, many: true),
        Field.Nested("output", IntentShape.Of(
            Field.Text("copy"),
            Field.Ids("insert", IdRole.Reference)), required: true, many: true),
        Field.Ids("inflectionClasses", IdRole.Reference),
        Field.Nested("placement", IntentShape.Of(
            Field.Id("after", IdRole.Reference),
            Field.Id("before", IdRole.Reference))));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_add_affix_process_allomorph");

    /// <inheritdoc />
    public override AuthorAffixProcessAllomorphIntent Parse(JsonElement authored) =>
        AuthorAffixProcessAllomorphIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorAffixProcessAllomorphIntent intent) =>
        AuthorAffixProcessAllomorphComposer.Build(context.Cache, intent, context.MintId);
}
