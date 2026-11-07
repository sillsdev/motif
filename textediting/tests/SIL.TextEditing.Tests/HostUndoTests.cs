using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>The host owns undo: the editor reports deltas and applies what the host hands back.</summary>
    public class HostUndoTests
    {
        [Fact]
        public void Each_keystroke_is_one_delta_and_host_undo_restores_the_whole_typing_step()
        {
            var e = new DocumentEditor(OneParagraph("hello world"));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            var before = Interchange.Json(e.Document);
            e.SetCaret(new TextPosition(0, 11));
            foreach (var c in "abc") e.TypeText(c.ToString());
            Assert.Equal(3, undo.Done.Count);
            Assert.Equal("hello worldabc", e.Document.Paragraphs[0].Text);

            Assert.Equal(1, undo.DoneSteps);
            undo.Undo();
            Assert.Equal("hello world", e.Document.Paragraphs[0].Text);
            Assert.Equal(11, e.Caret.Offset);
            Assert.Equal(before, Interchange.Json(e.Document));
            undo.Redo();
            Assert.Equal("hello worldabc", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_new_edit_clears_the_redo_list()
        {
            var e = new DocumentEditor(OneParagraph("x"));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 1));
            e.TypeText("a");
            undo.Undo();
            Assert.Single(undo.Undone);
            e.TypeText("b");
            Assert.Empty(undo.Undone);
            Assert.False(undo.Redo());
        }

        [Fact]
        public void External_insert_before_the_caret_moves_the_caret_with_the_text()
        {
            var e = new DocumentEditor(OneParagraph("hello world"));
            e.SetCaret(new TextPosition(0, 8));
            e.TypeText("X");
            Assert.Equal(9, e.Caret.Offset);
            var replacement = e.Document.Paragraphs[0].Clone();
            replacement.Insert(0, ">>>", "en", null);
            e.ExternalReplaceParagraph(0, replacement, 0, 0, 3);
            Assert.Equal(12, e.Caret.Offset);
            Assert.Equal(">>>hello woXrld", e.Document.Paragraphs[0].Text);
            e.TypeText("Y");
            Assert.Equal(">>>hello woXYrld", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void External_change_after_the_caret_leaves_the_caret()
        {
            var e = new DocumentEditor(OneParagraph(">>>hello woXYrld"));
            e.SetCaret(new TextPosition(0, 13));
            var replacement = e.Document.Paragraphs[0].Clone();
            replacement.Delete(replacement.Length - 3, 3);
            e.ExternalReplaceParagraph(0, replacement, replacement.Length, 3, 0);
            Assert.Equal(13, e.Caret.Offset);
        }

        [Fact]
        public void External_change_to_another_paragraph_leaves_the_caret()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), Para("en", "en", "two")));
            e.SetCaret(new TextPosition(0, 2));
            var other = e.Document.Paragraphs[1].Clone();
            other.Insert(3, "!!", "en", null);
            e.ExternalReplaceParagraph(1, other, 3, 0, 2);
            Assert.Equal(new TextPosition(0, 2), e.Caret);
            Assert.Equal("two!!", e.Document.Paragraphs[1].Text);
        }

        [Fact]
        public void The_access_check_runs_before_any_change_and_its_exception_leaves_the_document_alone()
        {
            var e = new DocumentEditor(OneParagraph("ab"));
            e.SetCaret(new TextPosition(0, 2));
            e.AccessCheck = () => throw new System.InvalidOperationException("wrong thread");
            Assert.Throws<System.InvalidOperationException>(() => e.TypeText("x"));
            Assert.Throws<System.InvalidOperationException>(() => e.Enter());
            Assert.Throws<System.InvalidOperationException>(() => e.Backspace());
            Assert.Equal("ab", e.Document.Paragraphs[0].Text);
            e.AccessCheck = null;
            e.TypeText("c");
            Assert.Equal("abc", e.Document.Paragraphs[0].Text);
        }
    }
}
