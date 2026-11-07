using System;
using System.Linq;

namespace SIL.TextEditing
{
    // Context-menu information (ADR 0021): the editor describes a place; the field raises the event and the host builds the menu.
    public sealed partial class DocumentEditor
    {
        /// <summary>The host's word rule for the questions the editor answers about words: the word of a context request (<see cref="DescribeContext"/>)
        /// and a whole-word find (<see cref="FindOptions.WholeWord"/>). A field gives it the same rule it uses for the double-click and Ctrl+arrows
        /// (<c>RichTextField.WordRules</c>, else its spell checker's), so there is one word rule: a field sets this property itself whenever its
        /// <c>SpellChecking</c> or <c>WordRules</c> is set, so a host with a field sets those, not this. Null, the default, uses
        /// <see cref="DefaultWordRules"/>.</summary>
        public IWordRules? WordRules { get; set; }

        bool TryWordAt(Paragraph paragraph, string text, int index, out int start, out int end)
            => WordBreaks.TryWordAt(paragraph, index, WordRules, out start, out end);

        /// <summary>Describes the place at <paramref name="position"/> for a context menu. With <paramref name="atCaret"/> the
        /// place is an insertion point (a keyboard request): the character after it counts, and when that is not part of a word or
        /// an object, the one before it. Otherwise it is the character at the position (the one under the pointer).</summary>
        public ContextInfo DescribeContext(TextPosition position, bool atCaret, bool onText = true)
        {
            int pi = Math.Max(0, Math.Min(Document.Paragraphs.Count - 1, position.Paragraph));
            var para = Document.Paragraphs[pi];
            var text = para.Text;
            int offset = Math.Max(0, Math.Min(text.Length, position.Offset));
            var info = new ContextInfo { Position = new TextPosition(pi, offset) };

            // The character the place means: at the offset, or for an insertion point the one before when none follows.
            int ch = !onText ? -1 : offset < text.Length ? offset : text.Length > 0 ? text.Length - 1 : -1;
            int word0 = 0, word1 = 0;
            bool onWord = ch >= 0 && TryWordAt(para, text, ch, out word0, out word1);
            if (!onWord && atCaret && offset > 0 && offset <= text.Length)
            {
                ch = offset - 1;
                onWord = TryWordAt(para, text, ch, out word0, out word1);
            }
            if (onWord)
            {
                info.Word = text.Substring(word0, word1 - word0);
                info.WordRange = new TextRange(pi, word0, word1);
            }
            else info.WordRange = new TextRange(pi, offset, offset);

            // The run: the one that holds the character the place means (the same one as the word starts in, when there is a word).
            int at = onWord ? word0 : ch;
            Run? run = null;
            int runStart = 0;
            if (at >= 0) run = para.RunAfter(at, out runStart);
            if (run != null)
            {
                info.Run = run.Clone();
                info.RunRange = new TextRange(pi, runStart, runStart + run.Text.Length);
                info.WritingSystem = run.Ws;
            }
            else info.WritingSystem = para.DefaultWs;

            // An embedded object: the one the place means, which for an insertion point is the one after it, else the one before.
            int obj = -1;
            if (onText && offset < text.Length && text[offset] == Run.ObjectReplacement) obj = offset;
            else if (atCaret && offset > 0 && text[offset - 1] == Run.ObjectReplacement) obj = offset - 1;
            if (obj >= 0)
            {
                var o = para.RunAfter(obj, out _);
                if (o != null && o.IsObject) info.EmbeddedObject = new EmbeddedObjectInfo(new TextRange(pi, obj, obj + 1), o.ObjData!);
            }

            Ordered(out var a, out var b);
            info.Selection = new TextRange(a, b);
            if (HasSelection) info.SelectedText = string.Join("\n", GetFragment().Select(p => p.Text));
            return info;
        }
    }
}
