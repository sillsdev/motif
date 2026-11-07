namespace SIL.TextEditing
{
    public sealed partial class DocumentEditor
    {
        /// <summary>The character properties text typed now would get (in the writing system it would get): over a selection, the ones the
        /// position where the selection began has once the selection is gone; otherwise the run before the caret's, else the one after it,
        /// when the run takes typing, else none. A copy, so the caller can build a run from it; the field draws an input method's composition in
        /// them, since they are what the committed text will have.</summary>
        public PropertyBag TypingProperties() => TypingRun().Props;

        /// <summary>An empty run with the writing system and properties text typed now would get. Typing over a selection deletes it first,
        /// so the run is the one at the start of the selection with the selection gone, as in FieldWorks; this is worked out on a copy and
        /// changes nothing.</summary>
        public Run TypingRun()
        {
            if (!HasSelection) return RunAt(this, Caret);
            Ordered(out var a, out var b);
            var scratch = new Document { Normalization = Document.Normalization };
            for (int i = a.Paragraph; i <= b.Paragraph; i++) scratch.Paragraphs.Add(Document.Paragraphs[i].Clone());
            var trial = new DocumentEditor(scratch) { Anchor = new TextPosition(0, a.Offset), Caret = new TextPosition(b.Paragraph - a.Paragraph, b.Offset) };
            trial.DeleteSelection("type");
            return trial.HasSelection || !trial.Caret.Equals(new TextPosition(0, a.Offset)) ? RunAt(this, Caret) : RunAt(trial, trial.Caret);
        }

        static Run RunAt(DocumentEditor editor, TextPosition at) =>
            new Run { Ws = editor.ActiveWs, Props = editor.InheritProps(at, editor.ActiveWs)?.Clone() ?? new PropertyBag() };
    }
}
