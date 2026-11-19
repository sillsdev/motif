using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>Creates an endocentric or exocentric compound rule joining two stem categories.</summary>
internal sealed class AuthorCompoundRuleDefinition : ComposerDefinition<AuthorCompoundRuleIntent>
{
    public override string Verb => "compose-author-compound-rule";
    public override string Composer => "AuthorCompoundRule";
    public override IReadOnlyList<string> Subjects { get; } = ["compounding"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Choice("kind", [CompoundRulePayload.Endocentric, CompoundRulePayload.Exocentric], required: true),
        Field.Text("name"),
        Field.Id("left", IdRole.Reference, required: true),
        Field.Id("right", IdRole.Reference, required: true),
        Field.Choice("head", AuthorCompoundRuleIntentParser.Heads),
        Field.Id("result", IdRole.Reference));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.Released, AgentClass.Draft, ToolName: "motif_author_compound_rule");
    public override AuthorCompoundRuleIntent Parse(JsonElement authored) => AuthorCompoundRuleIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorCompoundRuleIntent intent) =>
        AuthorCompoundRuleComposer.Build(context.Cache, intent, context.MintId);
}
