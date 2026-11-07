using System;
using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0023: consecutive typing is one undo step, reported to the host as one task.</summary>
    public class UndoGroupTests
    {
        sealed class Rig
        {
            public DocumentEditor Editor;
            public SimpleUndoStack Undo = new SimpleUndoStack();
            public List<EditDelta> Deltas = new List<EditDelta>();
            public List<long> Ended = new List<long>();
            public DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

            public Rig(string text)
            {
                Editor = new DocumentEditor(OneParagraph(text)) { Clock = () => Now };
                Undo.Attach(Editor);
                Editor.Committed += Deltas.Add;
                Editor.UndoGroupEnded += Ended.Add;
                Editor.SetCaret(new TextPosition(0, text.Length));
            }

            public void Type(string s) { foreach (var c in s) Editor.TypeText(c.ToString()); }
            public string Text => Editor.Document.Paragraphs[0].Text;
        }

        [Fact]
        public void Consecutive_typing_is_one_group_and_the_host_sees_one_start_and_one_end()
        {
            var r = new Rig("ab");
            r.Type("xyz");
            Assert.Equal(3, r.Deltas.Count);
            Assert.True(r.Deltas[0].StartsUndoGroup);
            Assert.False(r.Deltas[1].StartsUndoGroup);
            Assert.False(r.Deltas[2].StartsUndoGroup);
            Assert.Equal(r.Deltas[0].UndoGroup, r.Deltas[2].UndoGroup);
            Assert.Empty(r.Ended);
            Assert.True(r.Editor.HasOpenUndoGroup);

            r.Editor.CloseUndoGroup();
            Assert.Equal(new[] { r.Deltas[0].UndoGroup }, r.Ended);
            Assert.False(r.Editor.HasOpenUndoGroup);
        }

        [Fact]
        public void A_caret_move_ends_the_group_but_clicking_where_the_caret_already_is_does_not()
        {
            var r = new Rig("ab");
            r.Type("x");
            r.Editor.SetCaret(r.Editor.Caret);
            r.Type("y");
            Assert.Equal(r.Deltas[0].UndoGroup, r.Deltas[1].UndoGroup);
            r.Editor.SetCaret(new TextPosition(0, 1));
            Assert.Single(r.Ended);
            r.Type("z");
            Assert.True(r.Deltas[2].StartsUndoGroup);
            Assert.Equal("azbxy".Length, r.Text.Length);
            Assert.Equal(2, r.Undo.DoneSteps);
        }

        [Fact]
        public void A_pause_of_a_second_ends_the_group_and_less_does_not()
        {
            var r = new Rig("ab");
            r.Type("x");
            r.Now += TimeSpan.FromMilliseconds(900);
            r.Type("y");
            Assert.False(r.Deltas[1].StartsUndoGroup);
            r.Now += TimeSpan.FromMilliseconds(1100);
            r.Type("z");
            Assert.True(r.Deltas[2].StartsUndoGroup);
            Assert.Equal(2, r.Undo.DoneSteps);
            r.Undo.Undo();
            Assert.Equal("abxy", r.Text);
        }

        [Fact]
        public void A_change_of_writing_system_ends_the_group()
        {
            var r = new Rig("ab");
            r.Type("x");
            r.Editor.SetActiveWs("fr");
            r.Type("y");
            Assert.True(r.Deltas[1].StartsUndoGroup);
            Assert.Equal(2, r.Undo.DoneSteps);
        }

        [Fact]
        public void A_delete_after_typing_is_a_step_of_its_own_and_so_is_the_typing_after_it()
        {
            var r = new Rig("ab");
            r.Type("xy");
            r.Editor.Backspace();
            r.Type("z");
            Assert.Equal(3, r.Undo.DoneSteps);
            Assert.True(r.Deltas[2].StartsUndoGroup);
            Assert.True(r.Deltas[3].StartsUndoGroup);
            Assert.Equal(new[] { r.Deltas[0].UndoGroup, r.Deltas[2].UndoGroup }, r.Ended);   // the delete is one complete step at once
            r.Undo.Undo();
            Assert.Equal("abx", r.Text);
            r.Undo.Undo();
            Assert.Equal("abxy", r.Text);
            r.Undo.Undo();
            Assert.Equal("ab", r.Text);
        }

        [Fact]
        public void Typing_over_a_selection_is_one_step_with_the_deletion_and_never_joins_earlier_typing()
        {
            var r = new Rig("abcd");
            r.Type("x");
            r.Editor.Anchor = new TextPosition(0, 0);
            r.Editor.SetCaret(new TextPosition(0, 2), true);
            r.Type("QR");
            Assert.Equal("QRcdx", r.Text);
            Assert.Equal(2, r.Undo.DoneSteps);
            r.Undo.Undo();
            Assert.Equal("abcdx", r.Text);
            Assert.Equal(new TextPosition(0, 2), r.Editor.Caret);
        }

        [Fact]
        public void A_command_the_host_applies_ends_the_group_and_undo_redo_move_whole_steps()
        {
            var r = new Rig("ab");
            r.Type("xy");
            var inverse = r.Deltas[1].Inverse();
            r.Editor.Apply(inverse);   // the host changes the text itself: that closes the open typing step
            Assert.False(r.Editor.HasOpenUndoGroup);
            Assert.Single(r.Ended);
        }

        [Fact]
        public void Redo_replays_a_whole_step_and_a_new_edit_clears_the_redo_list()
        {
            var r = new Rig("ab");
            r.Type("xyz");
            r.Undo.Undo();
            Assert.Equal("ab", r.Text);
            Assert.Equal(1, r.Undo.UndoneSteps);
            Assert.True(r.Undo.Redo());
            Assert.Equal("abxyz", r.Text);
            r.Undo.Undo();
            r.Type("q");
            Assert.Equal(0, r.Undo.UndoneSteps);
            Assert.False(r.Undo.Redo());
        }

        [Fact]
        public void Enter_and_a_newline_in_typed_text_each_end_the_group()
        {
            var r = new Rig("ab");
            r.Editor.TypeText("x");
            r.Editor.TypeText("\ny");
            Assert.Equal(3, r.Undo.DoneSteps);
        }

        [Fact]
        public void Text_that_may_not_be_edited_opens_no_group()
        {
            var r = new Rig("ab");
            r.Editor.IsReadOnly = true;
            r.Type("x");
            Assert.Empty(r.Deltas);
            Assert.False(r.Editor.HasOpenUndoGroup);
        }

        [Fact]
        public void Pasted_text_is_its_own_undo_step_and_neither_joins_the_typing_before_nor_after_it()
        {
            var r = new Rig("");
            r.Type("ab");
            r.Editor.PasteText("PASTED");
            r.Type("cd");
            Assert.Equal("abPASTEDcd", r.Text);
            Assert.Equal(3, r.Undo.DoneSteps);
            r.Undo.Undo();
            Assert.Equal("abPASTED", r.Text);
            r.Undo.Undo();
            Assert.Equal("ab", r.Text);
        }

        [Fact]
        public void A_pasted_multi_line_text_is_one_undo_step_and_restores_everything_in_one_undo()
        {
            var r = new Rig("");
            r.Type("ab");
            r.Editor.PasteText("l1\nl2\r\nl3\nl4");
            Assert.Equal(4, r.Editor.Document.Paragraphs.Count);
            Assert.Equal(2, r.Undo.DoneSteps);
            Assert.False(r.Editor.HasOpenUndoGroup);
            r.Undo.Undo();
            Assert.Single(r.Editor.Document.Paragraphs);
            Assert.Equal("ab", r.Text);
        }

        [Fact]
        public void Pasting_text_over_a_selection_is_one_step_with_the_deletion()
        {
            var r = new Rig("hello world");
            r.Editor.SetCaret(new TextPosition(0, 0));
            r.Editor.SetCaret(new TextPosition(0, 5), true);
            r.Editor.PasteText("a\nb");
            Assert.Equal(1, r.Undo.DoneSteps);
            r.Undo.Undo();
            Assert.Equal("hello world", r.Text);
        }
    }
}
