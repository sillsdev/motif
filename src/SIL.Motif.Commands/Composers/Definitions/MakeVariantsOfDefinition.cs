using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Makes many existing entries variants of their main entries or senses, with one variant type.</summary>
internal sealed class MakeVariantsOfDefinition : ComposerDefinition<MakeVariantsOfIntent>
{
    public override string Verb => "compose-make-variants-of";
    public override string Composer => "MakeVariantsOf";
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("variantType", IdRole.Reference, required: true),
        Field.Nested("links", IntentShape.Of(
            Field.Id("entry", IdRole.Target, required: true),
            Field.Id("component", IdRole.Reference, required: true)), required: true, many: true),
        Field.Flag("onlyStubs"));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_make_variants_of");
    public override MakeVariantsOfIntent Parse(JsonElement authored) => MakeVariantsOfIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MakeVariantsOfIntent intent) =>
        MakeVariantsOfComposer.Build(context.Cache, intent, context.MintId);
}
