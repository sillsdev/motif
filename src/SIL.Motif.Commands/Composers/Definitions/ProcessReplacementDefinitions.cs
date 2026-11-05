using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

internal static class AdhocReferenceShape
{
    internal static IntentShape State { get; } = IntentShape.Of(
        Field.Choice("kind", ["allomorph", "morpheme"], required: true),
        Field.Id("first", required: true, nullable: true), Field.Ids("rest", required: true),
        Field.Ids("allomorphs", required: true), Field.Number("adjacency", required: true), Field.Flag("disabled", required: true));
}

internal sealed class EditAdhocProhibitionReferencesDefinition : ComposerDefinition<EditAdhocProhibitionReferencesIntent>
{
    public override string Verb => "compose-edit-adhoc-prohibition-references";
    public override string Composer => "EditAdhocProhibitionReferences";
    public override IReadOnlyList<string> Subjects { get; } = ["conditioned-allomorphy"];
    public override IntentShape Shape { get; } = IntentShape.Of(Field.Id("target", IdRole.Target, required: true),
        Field.Nested("expected", AdhocReferenceShape.State, required: true), Field.Nested("references", AdhocReferenceShape.State, required: true));
    public override ComposerExposure Exposure { get; } = new(CommandSurface.Released, ToolName: "motif_edit_adhoc_prohibition_references");
    public override EditAdhocProhibitionReferencesIntent Parse(JsonElement authored) => AdhocProhibitionReferencesIntentParser.Edit(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditAdhocProhibitionReferencesIntent intent) =>
        EditAdhocProhibitionReferencesComposer.Build(context.Cache, intent);
}

internal sealed class DeleteAdhocProhibitionDefinition : ComposerDefinition<DeleteAdhocProhibitionIntent>
{
    public override string Verb => "compose-delete-adhoc-prohibition";
    public override string Composer => "DeleteAdhocProhibition";
    public override IReadOnlyList<string> Subjects { get; } = ["conditioned-allomorphy"];
    public override IntentShape Shape { get; } = IntentShape.Of(Field.Id("target", IdRole.Target, required: true),
        Field.Nested("expected", AdhocReferenceShape.State, required: true));
    public override ComposerExposure Exposure { get; } = new(CommandSurface.Released, ToolName: "motif_delete_adhoc_prohibition");
    public override DeleteAdhocProhibitionIntent Parse(JsonElement authored) => AdhocProhibitionReferencesIntentParser.Delete(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteAdhocProhibitionIntent intent) =>
        DeleteAdhocProhibitionComposer.Build(context.Cache, intent);
}

internal sealed class DeleteAffixProcessDefinition : ComposerDefinition<DeleteAffixProcessIntent>
{
    public override string Verb => "compose-delete-affix-process";
    public override string Composer => "DeleteAffixProcess";
    public override IReadOnlyList<string> Subjects { get; } = ["conditioned-allomorphy", "infixation", "stem-alternation"];
    public override IntentShape Shape { get; } = IntentShape.Of(Field.Id("entry", required: true),
        Field.Id("form", IdRole.Target, required: true), Field.Id("replacement"));
    public override ComposerExposure Exposure { get; } = new(CommandSurface.Released, ToolName: "motif_delete_affix_process");
    public override DeleteAffixProcessIntent Parse(JsonElement authored) => DeleteAffixProcessIntentParser.Parse(authored);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteAffixProcessIntent intent) =>
        DeleteAffixProcessComposer.Build(context.Cache, intent);
}
