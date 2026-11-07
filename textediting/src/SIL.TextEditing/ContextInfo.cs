using System.Collections.Generic;

namespace SIL.TextEditing
{
    /// <summary>What a field knows about the place a context menu was asked for (ADR 0021): the word, the run, any link or
    /// embedded object, and the selection. The host builds the menu from it. Everything here is a copy: changing it changes
    /// nothing in the document.</summary>
    public sealed class ContextInfo
    {
        /// <summary>The place asked about: the character under the pointer, or the caret.</summary>
        public TextPosition Position { get; internal set; }

        /// <summary>The word at the place, or the empty string when it is on white space, punctuation or an embedded object. It is a word by the spell
        /// checker's rule when the field has one (the host's word-forming characters for each writing system, digits, and the apostrophes and joiners that
        /// stay inside a word) and by the same rule's defaults when not, so an underscore or a hyphen ends it in both, as in FieldWorks. An embedded object
        /// ends it here; the marked word of <see cref="Spelling"/> can have an object in it and so reach further.</summary>
        public string Word { get; internal set; } = string.Empty;

        /// <summary>The word's range; empty (at <see cref="Position"/>) when there is no word.</summary>
        public TextRange WordRange { get; internal set; }

        /// <summary>The writing system of the word, or of the text at the place when there is no word: the run's writing system, or
        /// the paragraph's default in an empty paragraph.</summary>
        public string WritingSystem { get; internal set; } = string.Empty;

        /// <summary>A copy of the run at the place (its text, writing system and properties), or null in an empty paragraph.</summary>
        public Run? Run { get; internal set; }

        /// <summary>Where that run lies; empty when <see cref="Run"/> is null.</summary>
        public TextRange RunRange { get; internal set; }

        /// <summary>The embedded object at the place, or null.</summary>
        public EmbeddedObjectInfo? EmbeddedObject { get; internal set; }

        /// <summary>The selection, in document order; empty at the caret when there is none.</summary>
        public TextRange Selection { get; internal set; }

        public bool HasSelection => !Selection.IsEmpty;

        /// <summary>The selected text, paragraphs joined by a line feed; empty when nothing is selected.</summary>
        public string SelectedText { get; internal set; } = string.Empty;

        /// <summary>Spelling suggestions for the word, best first, when the field has a spell checker (ADR 0020) and the word is marked; empty
        /// otherwise. The same strings as <see cref="Spelling"/>.All, kept as a plain list for a host that only lists them.</summary>
        public List<string> SpellingSuggestions { get; } = new List<string>();

        /// <summary>The spelling problem at the place, or null when the word is not marked or the field has no spell checker: the word
        /// the checker judged (which can differ from <see cref="Word"/>, as the checker's word rules are the host's), its writing system,
        /// why it is marked, all the suggestions, and what the menu's choices need (<c>RichTextField.ApplySpellingSuggestion</c>,
        /// <c>AddToDictionary</c>, <c>IgnoreWord</c>, <see cref="SIL.TextEditing.SpellingSuggestions.WithMissingSpaces"/>).</summary>
        public SIL.TextEditing.SpellingSuggestions? Spelling { get; set; }

        /// <summary>The language service's diagnostics at the place (ADR 0030), worst first; empty when there are none or the field has no language service. A host
        /// adds one menu item per diagnostic that has a <see cref="LanguageDiagnostic.Fix"/> and applies the choice with <c>RichTextField.ApplyDiagnosticFix</c>.</summary>
        public List<LanguageDiagnostic> Diagnostics { get; } = new List<LanguageDiagnostic>();
    }

    /// <summary>An embedded object (an ORC) at a place: its one-character range and the host's data for it.</summary>
    public sealed class EmbeddedObjectInfo
    {
        internal EmbeddedObjectInfo(TextRange range, string data)
        {
            Range = range;
            Data = data;
        }

        public TextRange Range { get; }

        /// <summary>The object data as the host stored it. Its first character is the kind: 3, 4 and 5 (name and GUID hot link,
        /// external path name, own name and GUID hot link) are links; other kinds are pictures and embedded objects.</summary>
        public string Data { get; }

        /// <summary>True for the hot-link kinds, which activate when clicked.</summary>
        public bool IsLink => Data.Length > 0 && Data[0] >= (char)3 && Data[0] <= (char)5;
    }
}
