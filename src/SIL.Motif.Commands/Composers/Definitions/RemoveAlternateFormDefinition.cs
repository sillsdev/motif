using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Removes one alternate form of an entry that nothing refers to.</summary>
internal sealed class RemoveAlternateFormDefinition : ComposerDefinition<RemoveAlternateFormIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-remove-alternate-form";

    /// <inheritdoc />
    public override string Composer => "RemoveAlternateForm";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Target, required: true),
        Field.Id("form", IdRole.Reference, required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_remove_alternate_form", AgentTool: false);

    /// <inheritdoc />
    public override RemoveAlternateFormIntent Parse(JsonElement authored) => RemoveAlternateFormIntentParser.Parse(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, RemoveAlternateFormIntent intent) =>
        RemoveAlternateFormComposer.Build(context.Cache, intent);
}
