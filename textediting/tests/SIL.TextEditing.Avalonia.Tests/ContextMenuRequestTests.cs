using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0021, ADR 0025: one context request gives one event; a host builds its menu from the event or sets Avalonia's
    /// ContextMenu on the field.</summary>
    public class ContextMenuRequestTests
    {
        static Point CenterOfFirstLine(Window window, RichTextField field)
        {
            var r = field.ParagraphLayoutFor(0).SelectionRects(0, 2)[0];
            return field.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), window)!.Value;
        }

        [Fact]
        public void A_right_click_raises_one_request_for_the_pointer_and_the_Menu_key_one_for_the_caret() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello world"));
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            var at = CenterOfFirstLine(window, field);
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Headless.Pump();
            Assert.Single(seen);
            Assert.Equal(ContextMenuTrigger.Pointer, seen[0].Trigger);
            Assert.Equal("hello", seen[0].Info.Word);

            field.Editor.SetCaret(new TextPosition(0, 8));
            field.Focus();
            Headless.Pump();
            field.Press(Key.Apps);
            Assert.Equal(2, seen.Count);
            Assert.Equal(ContextMenuTrigger.Keyboard, seen[1].Trigger);
            Assert.Equal("world", seen[1].Info.Word);
            Assert.True(seen[1].Position.Y >= field.CaretRect().Bottom - 1);
            window.Close();
        });

        [Fact]
        public void A_context_request_on_a_misspelled_word_carries_the_spelling_problem_and_its_suggestions() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello wrold"));
            var host = new SIL.TextEditing.Tests.FakeSpellHost("hello");
            host.Checkers["en"].Suggestions = new List<string> { "world", "wold" };
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            field.Editor.SetCaret(new TextPosition(0, 8));
            field.Focus();
            Headless.Pump();
            field.Press(Key.Apps);
            Assert.Single(seen);
            var info = seen[0].Info;
            Assert.Equal(new[] { "world", "wold" }, info.SpellingSuggestions);
            Assert.NotNull(info.Spelling);
            Assert.Equal("wrold", info.Spelling!.Word);
            Assert.Equal(SpellingProblem.Misspelled, info.Spelling.Problem);
            Assert.True(field.ApplySpellingSuggestion(info.Spelling, "world"));
            Assert.Equal("hello world", field.Editor.Document.Paragraphs[0].Text);
            // a correct word, and white space, carry none
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Press(Key.Apps);
            Assert.Null(seen[1].Info.Spelling);
            Assert.Empty(seen[1].Info.SpellingSuggestions);
            field.SpellChecking = null;
            field.Editor.SetCaret(new TextPosition(0, 8));
            field.Press(Key.Apps);
            Assert.Null(seen[2].Info.Spelling);
            window.Close();
        });

        [Fact]
        public void A_find_match_that_runs_over_several_lines_asks_the_host_to_show_all_of_it() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph(string.Join(" ", System.Linq.Enumerable.Repeat("word", 40))), 90);
            var targets = new List<Rect>();
            field.ScrollRequested += (o, e) => { if (e.Reason == ScrollReason.FindMatch) { targets.Add(e.Target); e.Handled = true; } };
            Assert.True(field.Find(new FindOptions { Pattern = "word word word word word word word" }));
            var match = field.CurrentMatch!.Value;
            var first = field.ParagraphLayoutFor(0).SelectionRects(match.Start.Offset, match.End.Offset);
            Assert.True(first.Count > 1, "the match should wrap");
            Assert.Single(targets);
            Assert.True(targets[0].Top <= first[0].Top + 0.5 && targets[0].Bottom >= first[first.Count - 1].Bottom - 0.5);
        });

        [Fact]
        public void A_slow_regular_expression_is_given_up_in_the_highlight_and_the_paint_never_runs_it_again() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph(new string('a', 40) + "!"));
            field.SetFindHighlight(new FindOptions { Pattern = "(a+)+$", UseRegularExpression = true, RegexTimeout = System.TimeSpan.FromMilliseconds(20) });
            Fixtures.Render(field);
            Assert.True(field.FindHighlightTimedOut);
            Assert.False(field.Editor.FindTimedOut);   // the paint has its own flag and leaves the host's search result alone
            Fixtures.Render(field);   // no second attempt: the flag stays until the options change
            Assert.True(field.FindHighlightTimedOut);
            field.SetFindHighlight(new FindOptions { Pattern = "a" });
            Assert.False(field.FindHighlightTimedOut);
        });

        [Fact]
        public void Shift_F10_gives_one_request_for_the_caret_and_other_F10_presses_are_left_alone() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello world"));
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            Assert.True(field.HandleKey(Key.F10, KeyModifiers.Shift));
            Assert.Single(seen);
            Assert.Equal(ContextMenuTrigger.Keyboard, seen[0].Trigger);
            Assert.False(field.HandleKey(Key.F10, KeyModifiers.Shift | KeyModifiers.Control));
            Assert.False(field.HandleKey(Key.F10, KeyModifiers.None));
            window.Close();
        });
    }
}
