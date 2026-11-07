using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Text typed over a selection takes the formatting the position where the selection began has once the selection is gone
    /// (the run before it, else the run after it), and <see cref="DocumentEditor.TypingRun"/> says what that is before anything is typed,
    /// so an input method's composition can be drawn in it.</summary>
    public class TypingOverSelectionFormatTests
    {
        static DocumentEditor Editor()
        {
            // Bold "abc", then plain "def", then bold "ghi", all one writing system.
            var doc = OneParagraph("abcdefghi");
            var p = doc.Paragraphs[0];
            p.Runs.Clear();
            p.Runs.Add(new Run { Ws = "en", Text = "abc" });
            p.Runs.Add(new Run { Ws = "en", Text = "def" });
            p.Runs.Add(new Run { Ws = "en", Text = "ghi" });
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Bold, 1);
            p.Runs[2].Props.SetInt(TextPropertyNumbers.Bold, 1);
            return new DocumentEditor(doc);
        }

        static bool IsBold(PropertyBag props) => props.TryGetInt(TextPropertyNumbers.Bold, out int value) && value == 1;

        static bool TypedIsBold(DocumentEditor e, int offset)
        {
            var p = e.Document.Paragraphs[0];
            int pos = 0;
            foreach (var r in p.Runs)
            {
                if (offset < pos + r.Text.Length) return IsBold(r.Props);
                pos += r.Text.Length;
            }
            throw new System.InvalidOperationException();
        }

        [Theory]
        [InlineData(1, 5, true)]     // bold "abc" then plain "def": the selection began inside the bold run
        [InlineData(5, 1, true)]     // the same selection made backwards
        [InlineData(3, 5, true)]     // began at the end of the bold run: the run before it
        [InlineData(0, 4, false)]    // began at the start of the paragraph: nothing before it, the run after the selection is plain
        [InlineData(4, 7, false)]    // began inside the plain run
        public void Typing_over_a_selection_takes_the_formatting_where_it_began_and_the_typing_run_says_so(int anchor, int caret, bool bold)
        {
            var e = Editor();
            e.SetCaret(new TextPosition(0, anchor));
            e.SetCaret(new TextPosition(0, caret), extend: true);
            Assert.Equal(bold, IsBold(e.TypingRun().Props));
            Assert.Equal(bold, IsBold(e.TypingProperties()));
            e.TypeText("X");
            int at = System.Math.Min(anchor, caret);
            Assert.Equal('X', e.Document.Paragraphs[0].Text[at]);
            Assert.Equal(bold, TypedIsBold(e, at));
        }

        [Fact]
        public void The_typing_run_names_the_writing_system_typed_text_gets_over_a_selection()
        {
            var doc = Doc(Para("en", "en", "abc", "fr", "def"));
            var e = new DocumentEditor(doc);
            e.SetCaret(new TextPosition(0, 0));
            e.SetCaret(new TextPosition(0, 4), extend: true);   // the first writing system it covers is "en", but "f" is what is left after the start
            var run = e.TypingRun();
            e.TypeText("X");
            Assert.Equal(run.Ws, doc.Paragraphs[0].Runs.First(r => r.Text.Contains("X")).Ws);
        }

        [Fact]
        public void Asking_for_the_typing_run_changes_nothing()
        {
            var e = Editor();
            e.SetCaret(new TextPosition(0, 1));
            e.SetCaret(new TextPosition(0, 5), extend: true);
            e.TypingRun();
            Assert.Equal("abcdefghi", e.Document.Paragraphs[0].Text);
            Assert.Equal(3, e.Document.Paragraphs[0].Runs.Count);
            Assert.Equal(new TextPosition(0, 5), e.Caret);
            Assert.Equal(new TextPosition(0, 1), e.Anchor);
        }
    }
}
