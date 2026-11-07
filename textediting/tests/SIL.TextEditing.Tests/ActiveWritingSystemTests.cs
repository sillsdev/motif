using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    public class ActiveWritingSystemTests
    {
        [Fact]
        public void Active_ws_follows_the_caret()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 9)); // inside "bonjour"
            Assert.Equal("fr", e.ActiveWs);
            e.SetCaret(new TextPosition(0, 2));
            Assert.Equal("en", e.ActiveWs);
        }

        [Fact]
        public void Typing_uses_the_chosen_ws_not_the_preceding_run()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 13)); // right after "bonjour"
            Assert.Equal("fr", e.ActiveWs);
            e.SetActiveWs("en");
            e.TypeText("X");
            Assert.Equal("en", WsOfCharAt(e, 0, 13));
        }

        [Fact]
        public void Typing_without_an_override_uses_the_run_at_the_caret()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 13));
            var expected = e.ActiveWs;
            e.TypeText("Y");
            Assert.Equal(expected, WsOfCharAt(e, 0, 13));
        }

        [Fact]
        public void Empty_paragraph_uses_its_default_ws()
        {
            var e = new DocumentEditor(Doc(Para("ta"), Para("en", "en", "x")));
            e.SetCaret(new TextPosition(0, 0));
            Assert.Equal("ta", e.ActiveWs);
        }

        [Fact]
        public void Selection_reports_every_ws_it_touches()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 2));
            e.SetCaret(new TextPosition(0, 10), true);
            Assert.Equal(new[] { "en", "fr" }, e.SelectionWsSet());
            e.SetCaret(new TextPosition(0, 7));
            e.SetCaret(new TextPosition(0, 11), true);
            Assert.Equal(new[] { "fr" }, e.SelectionWsSet());
        }

        [Fact]
        public void Active_ws_change_is_raised_once_with_old_and_new()
        {
            var e = new DocumentEditor(Basic());
            string? seen = null;
            e.ActiveWsChanged += (o, n) => seen = o + ">" + n;
            e.SetCaret(new TextPosition(0, 9));
            Assert.Equal("en>fr", seen);
        }

        static System.Collections.Generic.List<string> Watch(DocumentEditor e)
        {
            var seen = new System.Collections.Generic.List<string>();
            e.ActiveWsChanged += (o, n) => seen.Add(o + ">" + n);
            return seen;
        }

        [Fact]
        public void A_selection_takes_the_first_writing_system_it_covers_so_extending_it_forward_into_another_changes_nothing()
        {
            // FieldWorks sets the keyboard for the first writing system of a selection, not for the end the caret is at.
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 2));   // "Hello " is en
            var seen = Watch(e);
            e.SetCaret(new TextPosition(0, 9), true);    // into "bonjour" (fr)
            e.SetCaret(new TextPosition(0, 17), true);   // on into " world" (en)
            Assert.Equal("en", e.ActiveWs);
            Assert.Empty(seen);
            e.SetCaret(new TextPosition(0, 9));          // the selection collapses into fr
            Assert.Equal(new[] { "en>fr" }, seen);
        }

        [Fact]
        public void A_selection_that_starts_in_one_writing_system_and_ends_in_another_announces_the_first_when_it_is_made_from_the_end()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 16));         // " world" (en)
            var seen = Watch(e);
            e.SetCaret(new TextPosition(0, 9), true);    // back into "bonjour": the selection [9, 16) starts in fr
            Assert.Equal("fr", e.ActiveWs);
            e.SetCaret(new TextPosition(0, 2), true);    // back into "Hello ": the selection [2, 16) starts in en
            Assert.Equal("en", e.ActiveWs);
            Assert.Equal(new[] { "en>fr", "fr>en" }, seen);
        }

        [Fact]
        public void Choosing_the_typing_writing_system_announces_old_and_new_once_and_choosing_it_again_announces_nothing()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 2));
            var seen = Watch(e);
            e.SetActiveWs("fr");
            e.SetActiveWs("fr");
            e.SetCaret(new TextPosition(0, 2));          // back to the run's own writing system
            e.SetCaret(new TextPosition(0, 3));          // moving inside the same run says nothing more
            Assert.Equal(new[] { "en>fr", "fr>en" }, seen);
        }

        [Fact]
        public void An_edit_that_leaves_the_caret_in_another_writing_system_announces_it_once_and_undo_announces_the_return()
        {
            var e = new DocumentEditor(Basic());
            var deltas = new System.Collections.Generic.List<EditDelta>();
            e.Committed += deltas.Add;
            e.SetCaret(new TextPosition(0, 13));         // the end of "bonjour"
            var seen = Watch(e);
            for (int i = 0; i < 7; i++) e.Backspace();   // the word goes; the caret ends in "Hello "
            Assert.Equal(new[] { "fr>en" }, seen);
            seen.Clear();
            for (int i = deltas.Count - 1; i >= 0; i--)
                e.Apply(new EditDelta { Start = deltas[i].Start, Before = deltas[i].After, After = deltas[i].Before, CaretAfter = deltas[i].CaretBefore });
            Assert.Equal("fr", e.ActiveWs);
            Assert.Equal(new[] { "en>fr" }, seen);       // the undo steps one at a time through the word, announced when it is entered
        }

        static string? WsOfCharAt(DocumentEditor e, int paragraph, int offset)
            => e.Document.Paragraphs[paragraph].RunAfter(offset, out _)?.Ws;
    }
}
