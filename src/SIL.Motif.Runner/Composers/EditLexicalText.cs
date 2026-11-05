using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One writing-system alternative, guarded by its current plain text; empty replacement text clears it.</summary>
public sealed record LexicalTextAlternative(string WritingSystem, string ExpectedText, string Text);

/// <summary>An existing lexical object's Definition, Gloss or named note with individually guarded alternatives.</summary>
public sealed record EditLexicalTextIntent(CanonicalId Target, IReadOnlyList<LexicalTextAlternative> Alternatives,
    string? Field = null);

/// <summary>Reads text verbatim, including empty strings and whitespace, without adding linguistic policy.</summary>
public static class EditLexicalTextIntentParser
{
    public static EditLexicalTextIntent Parse(JsonElement input, bool note = false)
    {
        SoundSystemIntentParsing.Object(input, "EditLexicalText", note
            ? ["target", "alternatives", "field"] : ["target", "alternatives"]);
        var alternatives = SoundSystemIntentParsing.Array(input, "alternatives").Select(item =>
        {
            SoundSystemIntentParsing.Object(item, "text alternative", "writingSystem", "expectedText", "text");
            return new LexicalTextAlternative(SoundSystemIntentParsing.Text(item, "writingSystem"),
                SoundSystemIntentParsing.Text(item, "expectedText"), SoundSystemIntentParsing.Text(item, "text"));
        }).ToArray();
        if (alternatives.Length == 0)
            throw new ContractParseException("'alternatives' must contain at least one writing system.");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")), alternatives,
            note ? SoundSystemIntentParsing.Text(input, "field") : null);
    }
}

/// <summary>
/// Compares every authored alternative before returning unconditional semantic operations. Guards belong to
/// composition; the Proposal's Dry Run anchor protects subsequent Apply, as for RewriteLexemeForm.
/// </summary>
public static class EditLexicalTextComposer
{
    public static IReadOnlyList<string> NoteFields { get; } = Array.AsReadOnly(new[]
    {
        "note", "anthropology-note", "discourse-note", "general-note", "grammar-note", "phonology-note",
        "semantics-note", "sociolinguistics-note",
    });

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditLexicalTextIntent intent, string field)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Alternatives.Count == 0)
            throw new InvalidOperationException("At least one text alternative is required.");
        var target = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, "EditLexicalText");
        var (accessor, setKind, clearKind) = Field(target, field);
        var seen = new HashSet<int>();
        var operations = new List<OperationEnvelope>();
        foreach (var alternative in intent.Alternatives)
        {
            var ws = cache.WritingSystemFactory.GetWsFromStr(alternative.WritingSystem);
            if (ws == 0)
                throw new InvalidOperationException($"Writing system '{alternative.WritingSystem}' is not known to this project.");
            if (!seen.Add(ws))
                throw new InvalidOperationException($"Writing system '{alternative.WritingSystem}' appears twice.");
            var current = accessor.get_String(ws)?.Text ?? string.Empty;
            if (!Same(current, alternative.ExpectedText))
                throw new InvalidOperationException(
                    $"'{field}' on '{intent.Target.Value}' holds '{current}' in '{alternative.WritingSystem}', " +
                    $"not the expected '{alternative.ExpectedText}'.");
            if (Same(current, alternative.Text)) continue;
            var clearing = alternative.Text.Length == 0;
            var after = clearing
                ? JsonSerializer.SerializeToElement(new { ws = alternative.WritingSystem })
                : JsonSerializer.SerializeToElement(new { ws = alternative.WritingSystem, text = alternative.Text });
            operations.Add(new(CanonicalId.Mint(), clearing ? clearKind : setKind, target: intent.Target,
                after: after, rationale: $"Author the {field} text in the selected writing system."));
        }
        return operations;
    }

    private static bool Same(string left, string right) => string.Equals(left.Normalize(NormalizationForm.FormD),
        right.Normalize(NormalizationForm.FormD), StringComparison.Ordinal);

    private static (ITsMultiString Accessor, string Set, string Clear) Field(ICmObject target, string field) =>
        (target, field) switch
        {
            (ILexEntry entry, "note") => (entry.Comment,
                LexEntryCommentOperationKinds.SetComment, LexEntryCommentOperationKinds.ClearComment),
            (ILexSense sense, "definition") => (sense.Definition,
                LexSenseDefinitionOperationKinds.SetDefinition, LexSenseDefinitionOperationKinds.ClearDefinition),
            (ILexSense sense, "gloss") => (sense.Gloss,
                LexicalSenseOperationKinds.SetGloss, LexicalSenseOperationKinds.ClearGloss),
            (ILexSense sense, "anthropology-note") => (sense.AnthroNote,
                LexSenseAnthroNoteOperationKinds.SetAnthroNote, LexSenseAnthroNoteOperationKinds.ClearAnthroNote),
            (ILexSense sense, "discourse-note") => (sense.DiscourseNote,
                LexSenseDiscourseNoteOperationKinds.SetDiscourseNote, LexSenseDiscourseNoteOperationKinds.ClearDiscourseNote),
            (ILexSense sense, "general-note") => (sense.GeneralNote,
                LexSenseGeneralNoteOperationKinds.SetGeneralNote, LexSenseGeneralNoteOperationKinds.ClearGeneralNote),
            (ILexSense sense, "grammar-note") => (sense.GrammarNote,
                LexSenseGrammarNoteOperationKinds.SetGrammarNote, LexSenseGrammarNoteOperationKinds.ClearGrammarNote),
            (ILexSense sense, "phonology-note") => (sense.PhonologyNote,
                LexSensePhonologyNoteOperationKinds.SetPhonologyNote, LexSensePhonologyNoteOperationKinds.ClearPhonologyNote),
            (ILexSense sense, "semantics-note") => (sense.SemanticsNote,
                LexSenseSemanticsNoteOperationKinds.SetSemanticsNote, LexSenseSemanticsNoteOperationKinds.ClearSemanticsNote),
            (ILexSense sense, "sociolinguistics-note") => (sense.SocioLinguisticsNote,
                LexSenseSocioLinguisticsNoteOperationKinds.SetSocioLinguisticsNote,
                LexSenseSocioLinguisticsNoteOperationKinds.ClearSocioLinguisticsNote),
            (IMoForm, _) => throw new InvalidOperationException(
                "FieldWorks has no built-in allomorph Note field. Use the owning entry's Note and identify the allomorph in its text."),
            _ => throw new InvalidOperationException($"'{field}' is not a supported text field on {target.ClassName}."),
        };
}
