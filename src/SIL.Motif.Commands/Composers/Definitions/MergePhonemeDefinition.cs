using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>
/// Merges one phoneme into another so each spelling of the first becomes another way to write the second, and the
/// first phoneme is removed.
/// </summary>
internal sealed class MergePhonemeDefinition : ComposerDefinition<MergePhonemeIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-merge-phoneme";

    /// <inheritdoc />
    public override string Composer => "MergePhoneme";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("source", IdRole.Reference, required: true),
        Field.Id("target", IdRole.Target, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_merge_phoneme");

    /// <inheritdoc />
    public override MergePhonemeIntent Parse(JsonElement authored) => MergePhonemeIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, MergePhonemeIntent intent) =>
        MergePhonemeComposer.Build(context.Cache, intent, context.MintId);

    /// <inheritdoc />
    public override IReadOnlyList<ComposedRelatedObject>? RelatedObjects(LcmCache cache, MergePhonemeIntent intent) =>
        MergePhonemeComposer.ReadAffectedUsers(cache, intent);
}
