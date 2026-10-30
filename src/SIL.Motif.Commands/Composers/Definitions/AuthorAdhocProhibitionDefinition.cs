using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>Creates a morpheme or allomorph co-occurrence prohibition, FieldWorks' ad hoc rule.</summary>
internal sealed class AuthorAdhocProhibitionDefinition : ComposerDefinition<AuthorAdhocProhibitionIntent>
{
    public override string Verb => "compose-author-adhoc-prohibition";
    public override string Composer => "AuthorAdhocProhibition";
    public override IReadOnlyList<string> Subjects { get; } = ["conditioned-allomorphy"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Choice("kind", [AdhocProhibitionPayload.Morpheme, AdhocProhibitionPayload.Allomorph], required: true),
        Field.Id("first", IdRole.Reference, required: true),
        Field.Ids("rest", IdRole.Reference, required: true),
        Field.Choice("adjacency", AdhocProhibitionPayload.Adjacencies));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_author_adhoc_prohibition");
    public override AuthorAdhocProhibitionIntent Parse(JsonElement authored) => AuthorAdhocProhibitionIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorAdhocProhibitionIntent intent) =>
        AuthorAdhocProhibitionComposer.Build(context.Cache, intent, context.MintId);
}
