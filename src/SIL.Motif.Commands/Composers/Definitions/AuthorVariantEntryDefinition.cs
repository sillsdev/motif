using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Creates a sense-less variant of one sense, with its form, morph type, environments and variant type.</summary>
internal sealed class AuthorVariantEntryDefinition : ComposerDefinition<AuthorVariantEntryIntent>
{
    public override string Verb => "compose-author-variant-entry";
    public override string Composer => "AuthorVariantEntry";
    public override IReadOnlyList<string> Subjects { get; } = ["stem-alternation", "lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("component", IdRole.Reference, required: true),
        Field.Id("variantType", IdRole.Reference, required: true),
        Field.Text("form", required: true),
        Field.Text("formWs", required: true),
        Field.Id("morphType", IdRole.Reference, required: true),
        Field.Ids("environments"),
        Field.Flag("hideMinorEntry"));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_author_variant_entry");
    public override AuthorVariantEntryIntent Parse(JsonElement authored) => AuthorVariantEntryIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AuthorVariantEntryIntent intent) =>
        AuthorVariantEntryComposer.Build(context.Cache, intent, context.MintId);
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, AuthorVariantEntryIntent intent) =>
        AuthorVariantEntryComposer.ReadWarnings(cache, intent);
}
