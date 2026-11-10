using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0035: a source-mode return makes the field's paragraphs what the source says as one undo step, through paste's preparation, leaving the
    /// paragraphs it did not change alone and keeping the keys of those it did (ADR 0006).</summary>
    public class ReplaceParagraphsTests
    {
        static readonly Func<Paragraph, Paragraph, bool> SameText = (a, b) => a.Text == b.Text && a.Style == b.Style;

        static List<Paragraph> Read(params string[] lines) => lines.Select(l => Para("en", "en", l)).ToList();

        static (DocumentEditor Editor, SimpleUndoStack Undo) Three()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), Para("en", "en", "two"), Para("en", "en", "three")));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            return (e, undo);
        }

        [Fact]
        public void The_same_paragraphs_change_nothing_and_make_no_undo_step()
        {
            var (e, undo) = Three();
            var before = e.Document.Paragraphs.ToList();
            Assert.False(e.ReplaceParagraphs(Read("one", "two", "three"), SameText, "Edit as Markdown"));
            Assert.Equal(0, undo.DoneSteps);
            Assert.Equal(before, e.Document.Paragraphs);   // the very same objects
        }

        [Fact]
        public void A_changed_middle_paragraph_is_one_undo_step_and_keeps_its_key_and_its_neighbours()
        {
            var (e, undo) = Three();
            var first = e.Document.Paragraphs[0];
            var last = e.Document.Paragraphs[2];
            var deltas = new List<EditDelta>();
            e.Committed += deltas.Add;
            Assert.True(e.ReplaceParagraphs(Read("one", "TWO", "three"), SameText, "Edit as Markdown"));
            Assert.Equal(new[] { "one", "TWO", "three" }, e.Document.Paragraphs.Select(p => p.Text));
            Assert.Same(first, e.Document.Paragraphs[0]);
            Assert.Same(last, e.Document.Paragraphs[2]);
            Assert.Equal("key1", e.Document.Paragraphs[1].Key);
            var d = Assert.Single(deltas);
            Assert.Equal(1, d.Start);
            Assert.Single(d.Before);
            Assert.Equal("Edit as Markdown", d.Label);
            Assert.Equal(1, undo.DoneSteps);
            undo.Undo();
            Assert.Equal(new[] { "one", "two", "three" }, e.Document.Paragraphs.Select(p => p.Text));
            Assert.Equal("key1", e.Document.Paragraphs[1].Key);
        }

        [Fact]
        public void A_style_the_source_gives_is_taken_where_paste_would_keep_the_target_s()
        {
            var (e, _) = Three();
            var heading = Para("en", "en", "two");
            heading.Style = "Heading 1";
            var incoming = Read("one");
            incoming.Add(heading);
            incoming.Add(Para("en", "en", "three"));
            Assert.True(e.ReplaceParagraphs(incoming, SameText, "Edit as Markdown"));
            Assert.Equal("Heading 1", e.Document.Paragraphs[1].Style);
            Assert.Equal("key1", e.Document.Paragraphs[1].Key);
        }

        [Fact]
        public void Added_paragraphs_have_no_key_until_the_host_gives_one_and_removed_ones_go()
        {
            var (e, undo) = Three();
            Assert.True(e.ReplaceParagraphs(Read("one", "two", "two and a half", "three"), SameText, "Edit as Markdown"));
            Assert.Equal(new object?[] { "key0", "key1", null, "key2" }, e.Document.Paragraphs.Select(p => p.Key));
            Assert.True(e.ReplaceParagraphs(Read("one", "three"), SameText, "Edit as Markdown"));
            Assert.Equal(new object?[] { "key0", "key2" }, e.Document.Paragraphs.Select(p => p.Key));
            Assert.Equal(2, undo.DoneSteps);
            undo.Undo();
            undo.Undo();
            Assert.Equal(new[] { "one", "two", "three" }, e.Document.Paragraphs.Select(p => p.Text));
        }

        static (DocumentEditor Editor, SimpleUndoStack Undo) Of(params string[] texts)
        {
            var e = new DocumentEditor(Doc(texts.Select(t => Para("en", "en", t)).ToArray()));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            return (e, undo);
        }

        static object?[] Keys(DocumentEditor e) => e.Document.Paragraphs.Select(p => p.Key).ToArray();

        [Fact]
        public void A_paragraph_added_at_the_top_and_another_changed_lower_down_leave_every_key_on_its_own_text()
        {
            var (e, undo) = Of("A", "B", "C", "D");
            var kept = e.Document.Paragraphs.Take(3).ToList();
            Assert.True(e.ReplaceParagraphs(Read("New", "A", "B", "C", "Dx"), SameText, "Edit as Markdown"));
            Assert.Equal(new[] { "New", "A", "B", "C", "Dx" }, e.Document.Paragraphs.Select(p => p.Text));
            Assert.Equal(new object?[] { null, "key0", "key1", "key2", "key3" }, Keys(e));
            for (int i = 0; i < 3; i++) Assert.Same(kept[i], e.Document.Paragraphs[i + 1]);   // untouched: the very objects
            Assert.Equal(1, undo.DoneSteps);
            undo.Undo();
            Assert.Equal(new[] { "A", "B", "C", "D" }, e.Document.Paragraphs.Select(p => p.Text));
            Assert.Equal(new object?[] { "key0", "key1", "key2", "key3" }, Keys(e));
        }

        [Fact]
        public void Two_separate_edits_one_of_which_adds_a_paragraph_keep_the_keys_between_them()
        {
            var (e, _) = Of("A", "B", "C", "D", "E");
            Assert.True(e.ReplaceParagraphs(Read("A", "Bx", "B2", "C", "D", "Ex"), SameText, "Edit as Markdown"));
            Assert.Equal(new object?[] { "key0", "key1", null, "key2", "key3", "key4" }, Keys(e));
        }

        [Fact]
        public void Two_separate_edits_one_of_which_removes_a_paragraph_keep_the_keys_between_them()
        {
            var (e, _) = Of("A", "B", "C", "D", "E");
            Assert.True(e.ReplaceParagraphs(Read("Ax", "C", "D", "Ex"), SameText, "Edit as Markdown"));
            Assert.Equal(new[] { "Ax", "C", "D", "Ex" }, e.Document.Paragraphs.Select(p => p.Text));
            Assert.Equal(new object?[] { "key0", "key2", "key3", "key4" }, Keys(e));
        }

        [Fact]
        public void Deleting_one_paragraph_and_editing_another_gives_the_edited_one_its_own_key()
        {
            var (e, _) = Of("A", "B", "C", "D");
            var c = e.Document.Paragraphs[2];
            Assert.True(e.ReplaceParagraphs(Read("A", "C", "Dx"), SameText, "Edit as Markdown"));
            Assert.Equal(new object?[] { "key0", "key2", "key3" }, Keys(e));
            Assert.Same(c, e.Document.Paragraphs[1]);
        }

        [Fact]
        public void Edited_paragraphs_take_the_document_s_normalization_form()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "caf\u00e9"), Para("en", "en", "two")));
            e.Document.Normalization = NormalizationForm.FormC;
            Assert.True(e.ReplaceParagraphs(Read("cafe\u0301", "two cafe\u0301"), SameText, "Edit as Markdown"));
            Assert.Equal("caf\u00e9", e.Document.Paragraphs[0].Text);
            Assert.Equal("two caf\u00e9", e.Document.Paragraphs[1].Text);
        }

        [Fact]
        public void While_held_for_source_mode_every_edit_command_is_refused()
        {
            var (e, undo) = Of("one two", "three");
            e.HeldForSource = true;
            e.SetCaret(new TextPosition(0, 3));
            Assert.False(e.CanEdit);
            e.TypeText("x");
            e.PasteText("y");
            e.Enter();
            e.Backspace();
            e.DeleteForward();
            e.SetCaret(new TextPosition(0, 0));
            e.SetCaret(new TextPosition(0, 3), extend: true);
            Assert.False(e.CanFormatSelection);
            e.DeleteSelection();
            e.ReplaceRange(new TextPosition(0, 0), new TextPosition(0, 3), Read("z"), "replace");
            Assert.Equal(0, e.ReplaceAll(new FindOptions { Pattern = "one" }, "1"));
            Assert.False(e.ReplaceParagraphs(Read("changed"), SameText, "Edit as Markdown"));
            Assert.Equal(new[] { "one two", "three" }, e.Document.Paragraphs.Select(p => p.Text));
            Assert.Equal(0, undo.DoneSteps);
            e.HeldForSource = false;
            Assert.True(e.CanEdit);
        }

        [Fact]
        public void Nothing_left_gives_one_empty_paragraph()
        {
            var (e, _) = Three();
            Assert.True(e.ReplaceParagraphs(new List<Paragraph>(), SameText, "Edit as Markdown"));
            Assert.Equal("", Assert.Single(e.Document.Paragraphs).Text);
        }

        [Fact]
        public void A_read_only_field_or_text_that_may_not_be_edited_refuses()
        {
            var (e, undo) = Three();
            e.IsReadOnly = true;
            Assert.False(e.ReplaceParagraphs(Read("x"), SameText, "Edit as Markdown"));
            e.IsReadOnly = false;
            e.Document.Paragraphs[1].Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            Assert.False(e.ReplaceParagraphs(Read("one", "changed", "three"), SameText, "Edit as Markdown"));
            Assert.Equal(0, undo.DoneSteps);
        }

        [Fact]
        public void The_incoming_paragraphs_are_prepared_as_paste_prepares_them()
        {
            var e = new DocumentEditor(OneParagraph("one")) { SingleParagraph = true };
            e.Document.Normalization = NormalizationForm.FormD;
            e.Document.Paragraphs[0].Style = "Kept";
            var incoming = Read("café\tau", "lait");
            incoming[0].Style = "Other";
            Assert.True(e.ReplaceParagraphs(incoming, SameText, "Edit as Markdown"));
            var p = Assert.Single(e.Document.Paragraphs);
            Assert.Equal("caféau lait", p.Text);   // NFD, the tab removed, the two paragraphs joined with a space
            Assert.Equal("Kept", p.Style);              // a single-paragraph field keeps its paragraph's style
            Assert.Equal("key0", p.Key);
        }
    }
}
