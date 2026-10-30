using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Makes an existing entry a variant of another entry or sense by creating its variant reference.</summary>
internal sealed class MakeVariantOfDefinition : ComposerDefinition<MakeVariantOfIntent>
{
    public override string Verb => "compose-make-variant-of";
    public override string Composer => "MakeVariantOf";
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Target, required: true),
        Field.Id("component", IdRole.Reference, required: true),
        Field.Id("variantType", IdRole.Reference, required: true));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_make_variant_of");
    public override MakeVariantOfIntent Parse(JsonElement authored) => MakeVariantOfIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MakeVariantOfIntent intent) =>
        MakeVariantOfComposer.Build(context.Cache, intent, context.MintId);
}
