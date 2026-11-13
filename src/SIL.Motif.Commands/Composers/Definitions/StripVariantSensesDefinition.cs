using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Turns variant entries that copy their main sense into sense-less variants of that sense.</summary>
internal sealed class StripVariantSensesDefinition : ComposerDefinition<StripVariantSensesIntent>
{
    public override string Verb => "compose-strip-variant-senses";
    public override string Composer => "StripVariantSenses";
    public override IReadOnlyList<string> Subjects { get; } = ["stem-alternation", "lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Nested("entries", IntentShape.Of(
            Field.Id("entry", IdRole.Target, required: true),
            Field.Id("component", IdRole.Reference)), required: true, many: true));
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_strip_variant_senses");
    public override StripVariantSensesIntent Parse(JsonElement authored) => StripVariantSensesIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, StripVariantSensesIntent intent) =>
        StripVariantSensesComposer.Build(context.Cache, intent, context.MintId);
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, StripVariantSensesIntent intent) =>
        StripVariantSensesComposer.ReadDeltas(cache, intent);
}
