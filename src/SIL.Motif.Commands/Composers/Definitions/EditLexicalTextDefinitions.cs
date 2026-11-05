using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Stages individually guarded Definition, Gloss and lexical note alternatives through the shared engine.</summary>
internal sealed class EditLexicalTextDefinition(string textField) : ComposerDefinition<EditLexicalTextIntent>
{
    private bool IsNote => textField == "lexical-note";
    public override string Verb => "compose-edit-" + textField;
    public override string Composer => textField switch
    {
        "definition" => "EditDefinition",
        "gloss" => "EditGloss",
        _ => "EditLexicalNote",
    };
    public override IReadOnlyList<string> Subjects { get; } = ["lexical-entries-and-glosses"];
    public override IntentShape Shape { get; } = IntentShape.Of((textField == "lexical-note"
        ? new[] { Field.Choice("field", EditLexicalTextComposer.NoteFields, required: true) }
        : []).Concat(new[]
        {
            Field.Id("target", IdRole.Target, required: true),
            Field.Nested("alternatives", IntentShape.Of(
                Field.Text("writingSystem", required: true),
                Field.Text("expectedText", required: true),
                Field.Text("text", required: true)), required: true, many: true),
        }).ToArray());
    public override ComposerExposure Exposure { get; } = new(CommandSurface.AdvancedAi, AgentClass.Draft,
        ToolName: "motif_edit_" + textField.Replace('-', '_'));
    public override EditLexicalTextIntent Parse(JsonElement authored) => EditLexicalTextIntentParser.Parse(authored, IsNote);
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, EditLexicalTextIntent intent) =>
        EditLexicalTextComposer.Build(context.Cache, intent, IsNote ? intent.Field! : textField);
}
