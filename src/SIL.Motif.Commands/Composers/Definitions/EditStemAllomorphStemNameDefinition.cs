using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Sets or clears the stem name of one stem allomorph.</summary>
internal sealed class EditStemAllomorphStemNameDefinition : ComposerDefinition<EditStemAllomorphStemNameIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-stem-allomorph-stem-name";

    /// <inheritdoc />
    public override string Composer => "EditStemAllomorphStemName";

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Id("expectedStemName", required: true, nullable: true),
        Field.Id("stemName", required: true, nullable: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, OnByDefault: false, AgentTool: false);

    /// <inheritdoc />
    public override EditStemAllomorphStemNameIntent Parse(JsonElement authored) => EditStemAllomorphStemNameIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditStemAllomorphStemNameIntent intent) =>
        EditStemAllomorphStemNameComposer.Build(context.Cache, intent);
}
