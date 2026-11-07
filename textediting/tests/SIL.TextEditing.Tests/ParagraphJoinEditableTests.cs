using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0019: a paragraph join removes a paragraph mark, which belongs to both neighbours; neither may be
    /// text that may not be edited.</summary>
    public class ParagraphJoinEditableTests
    {
        static Paragraph Locked(string text)
        {
            var p = Para("en", "en", text);
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            return p;
        }

        static string[] Texts(DocumentEditor e) => e.Document.Paragraphs.ConvertAll(p => p.Text).ToArray();

        [Fact]
        public void Backspace_at_the_start_of_a_read_only_paragraph_does_not_merge_it_into_the_one_before()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "edit me"), Locked("readonly text")));
            e.SetCaret(new TextPosition(1, 0));
            Assert.Equal(new TextPosition(1, 0), e.Caret);
            e.Backspace();
            Assert.Equal(new[] { "edit me", "readonly text" }, Texts(e));
        }

        [Fact]
        public void Backspace_at_the_start_of_an_editable_paragraph_does_not_merge_it_into_a_read_only_one()
        {
            var e = new DocumentEditor(Doc(Locked("RO one"), Para("en", "en", "edit")));
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Equal(new[] { "RO one", "edit" }, Texts(e));
        }

        [Fact]
        public void Delete_at_the_end_of_an_editable_paragraph_does_not_merge_a_read_only_one_into_it()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "edit"), Locked("RO two")));
            e.SetCaret(new TextPosition(0, 4));
            e.DeleteForward();
            Assert.Equal(new[] { "edit", "RO two" }, Texts(e));
        }

        [Fact]
        public void Paragraphs_that_may_be_edited_still_join()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "ab"), Para("en", "en", "cd")));
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Equal(new[] { "abcd" }, Texts(e));
            e = new DocumentEditor(Doc(Para("en", "en", "ab"), Para("en", "en", "")));
            e.SetCaret(new TextPosition(0, 2));
            e.DeleteForward();
            Assert.Equal(new[] { "ab" }, Texts(e));
        }

        [Fact]
        public void A_selection_spanning_a_paragraph_mark_next_to_a_read_only_edge_is_refused()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "ab"), Locked("lock")));
            e.Anchor = new TextPosition(0, 2);
            e.SetCaret(new TextPosition(1, 0), true);
            Assert.False(e.CanEdit);
            e.DeleteSelection();
            Assert.Equal(new[] { "ab", "lock" }, Texts(e));
        }
    }
}
