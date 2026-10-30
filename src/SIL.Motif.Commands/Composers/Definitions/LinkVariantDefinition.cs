using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Links an existing variant reference to its main entry or sense and variant type.</summary>
internal sealed class LinkVariantDefinition : ComposerDefinition<LinkVariantIntent>
{
    public override string Verb => "compose-link-variant";
    public override string Composer => "LinkVariant";
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("component", IdRole.Reference, required: true),
        Field.Id("variantType", IdRole.Reference, required: true));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_link_variant");
    public override LinkVariantIntent Parse(JsonElement authored) => LinkVariantIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, LinkVariantIntent intent) =>
        LinkVariantComposer.Build(context.Cache, intent);
}
