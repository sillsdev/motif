using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>Moves one spelling of a phoneme out into a new phoneme, which undoes a merge.</summary>
internal sealed class SplitPhonemeDefinition : ComposerDefinition<SplitPhonemeIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-split-phoneme";

    /// <inheritdoc />
    public override string Composer => "SplitPhoneme";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("source", IdRole.Target, required: true),
        Field.Text("representation", required: true),
        Field.Text("name", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_split_phoneme");

    /// <inheritdoc />
    public override SplitPhonemeIntent Parse(JsonElement authored) => SplitPhonemeIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SplitPhonemeIntent intent) =>
        SplitPhonemeComposer.Build(context.Cache, intent, context.MintId);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, SplitPhonemeIntent intent) =>
        SplitPhonemeComposer.ReadAffectedUsers(cache, intent);
}
