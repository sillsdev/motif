using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Shift+Enter inserts a hard line break, U+2028, inside the paragraph, as in FieldWorks
    /// (docs/feature-inventory.md, "Hard line break within a paragraph").</summary>
    public class HardLineBreakTests
    {
        static readonly string LineSeparator = ((char)0x2028).ToString();

        static DocumentEditor Editor(string text, bool single = false)
        {
            var e = new DocumentEditor(OneParagraph(text)) { SingleParagraph = single };
            e.SetCaret(new TextPosition(0, 1));
            return e;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_line_break_goes_into_the_paragraph_without_splitting_it(bool single)
        {
            var e = Editor("abc", single);
            e.InsertLineBreak();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("a" + LineSeparator + "bc", e.Document.Paragraphs[0].Text);
            Assert.Equal(new TextPosition(0, 2), e.Caret);
        }

        [Fact]
        public void A_line_break_replaces_the_selection_in_one_undo_step_that_typing_does_not_join()
        {
            var e = Editor("abcd");
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.TypeText("x");
            e.SetCaret(new TextPosition(0, 1));
            e.SetCaret(new TextPosition(0, 3), true);
            e.InsertLineBreak();
            e.TypeText("y");
            Assert.Equal("a" + LineSeparator + "ycd", e.Document.Paragraphs[0].Text);
            Assert.Equal(3, undo.DoneSteps);
            undo.Undo();
            undo.Undo();
            Assert.Equal("axbcd", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_line_break_is_refused_where_the_text_may_not_be_edited()
        {
            var e = Editor("abc");
            e.IsReadOnly = true;
            e.InsertLineBreak();
            Assert.Equal("abc", e.Document.Paragraphs[0].Text);

            var p = Para("en", "en", "abc");
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            e = new DocumentEditor(Doc(p));
            e.SetCaret(new TextPosition(0, 1));
            e.InsertLineBreak();
            Assert.Equal("abc", e.Document.Paragraphs[0].Text);
        }
    }
}
