using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Edits the exact inflection class restrictions on one ordinary affix form.</summary>
internal sealed class EditAffixInflectionClassesDefinition : ComposerDefinition<EditAffixInflectionClassesIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-edit-affix-inflection-classes";

    /// <inheritdoc />
    public override string Composer => "EditAffixInflectionClasses";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Ids("expectedInflectionClasses", required: true),
        Field.Ids("inflectionClasses", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, AgentTool: false);

    /// <inheritdoc />
    public override EditAffixInflectionClassesIntent Parse(JsonElement authored) => EditAffixInflectionClassesIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditAffixInflectionClassesIntent intent) =>
        EditAffixInflectionClassesComposer.Build(context.Cache, intent);
}
