using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    public class SpellingFieldTests
    {
        static Document Doc3() => TestDocs.Doc(
            TestDocs.Para("en", "en", "The cat saat down"),
            TestDocs.Para("en", "en", "all good here"),
            TestDocs.Para("en", "en", "and teh end"));

        static FakeSpellHost Host() => new FakeSpellHost("The", "cat", "down", "all", "good", "here", "and", "end", "the", "seat", "sat");

        static RichTextField Field(Document doc, FakeSpellHost? host = null, double width = 520)
        {
            var field = Fixtures.Field(doc, width);
            // The idle post is a seam: nothing runs until the test says so.
            field.SpellIdlePost = a => { };
            field.SpellChecking = host ?? Host();
            return field;
        }

        static string[] Words(RichTextField f, int p) => f.SpellingMarks(p).Select(m => f.Editor.Document.Paragraphs[p].Text.Substring(m.Start, m.Length)).ToArray();

        [Fact]
        public void Nothing_is_marked_until_a_checking_step_runs_and_each_step_checks_one_visible_paragraph() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            Assert.False(f.IsSpellingComplete);
            Assert.Empty(f.SpellingMarks(0));
            Assert.False(f.CheckSpellingStep());
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            Assert.Empty(f.SpellingMarks(2));
            Assert.False(f.CheckSpellingStep());
            Assert.True(f.CheckSpellingStep());
            Assert.Equal(new[] { "teh" }, Words(f, 2));
            Assert.True(f.IsSpellingComplete);
            Assert.True(f.CheckSpellingStep());
        });

        [Fact]
        public void Setting_the_host_asks_for_idle_time_once_and_the_idle_work_steps_until_everything_is_checked() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var (window, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = posts.Add;
            f.SpellChecking = Host();
            Assert.Single(posts);
            f.SpellChecking = Host();   // a new host starts over, still one pending request
            Assert.Single(posts);
            int runs = 0;
            while (posts.Count > 0 && runs < 10)
            {
                var next = posts[0];
                posts.RemoveAt(0);
                next();
                runs++;
            }
            Assert.Equal(3, runs);
            Assert.True(f.IsSpellingComplete);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            window.Close();
        });

        [Fact]
        public void Without_a_host_or_with_the_host_removed_there_are_no_marks_and_no_idle_work() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var (window, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = posts.Add;
            Assert.Empty(posts);
            Assert.True(f.IsSpellingComplete);
            f.SpellChecking = Host();
            while (!f.CheckSpellingStep()) { }
            Assert.NotEmpty(f.SpellingMarks(0));
            f.SpellChecking = null;
            Assert.Empty(f.SpellingMarks(0));
            Assert.True(f.IsSpellingComplete);
            window.Close();
        });

        [Fact]
        public void An_edit_keeps_the_marks_outside_the_edited_word_moved_with_the_text_and_checks_only_around_the_edit() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Editor.TypeText("X");
            // The marks outside the edit stay and move with the text; only the word the edit touched is left to check.
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            Assert.Equal(new SpellingMark(9, 4, SpellingProblem.Misspelled), f.SpellingMarks(0)[0]);
            Assert.Equal(new[] { "teh" }, Words(f, 2));
            Assert.False(f.IsSpellingComplete);
            Assert.True(f.CheckSpellingStep());
            // "XThe" is not in the list, and "saat" is still marked where it moved to.
            Assert.Equal(new[] { "XThe", "saat" }, Words(f, 0));
            Assert.Equal(new SpellingMark(9, 4, SpellingProblem.Misspelled), f.SpellingMarks(0)[1]);
        });

        [Fact]
        public void Splitting_joining_and_inserting_paragraphs_keeps_marks_with_the_paragraph_they_describe() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            f.Editor.SetCaret(new TextPosition(1, 3));
            f.Editor.Enter();
            Assert.Equal(4, f.Editor.Document.Paragraphs.Count);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            Assert.Equal(new[] { "teh" }, Words(f, 3));
            Assert.Empty(f.SpellingMarks(1));
            Assert.Empty(f.SpellingMarks(2));
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(f.SpellingMarks(1));
            f.Editor.SetCaret(new TextPosition(2, 0));
            f.Editor.Backspace();
            Assert.Equal(3, f.Editor.Document.Paragraphs.Count);
            Assert.Equal(new[] { "teh" }, Words(f, 2));
        });

        [Fact]
        public void A_mark_is_never_drawn_on_text_it_was_not_made_for() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            Assert.Single(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
            // The host changes a paragraph behind the field's back and refreshes: the old result is not carried over.
            f.Editor.Document.Paragraphs[0].Runs[0].Text = "Brand new text";
            f.RefreshFromDocument();
            Assert.Empty(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
            Assert.Empty(f.SpellingMarks(0));
        });

        [Fact]
        public void A_mark_the_layout_draws_is_red_for_a_misspelled_word_and_blue_for_a_mixed_one() => Headless.Run(() =>
        {
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "saat ", "en", "ab", "fr", "cd"));
            var f = Field(doc);
            while (!f.CheckSpellingStep()) { }
            var drawn = f.ParagraphLayoutFor(0).SpellingMarksDrawn;
            Assert.Equal(2, drawn.Count);
            Assert.Equal((0, 4, Colors.Red), (drawn[0].Start, drawn[0].End, ((ISolidColorBrush)drawn[0].Brush).Color));
            Assert.Equal((5, 9, Colors.Blue), (drawn[1].Start, drawn[1].End, ((ISolidColorBrush)drawn[1].Brush).Color));
        });

        [Fact]
        public void Layout_changes_do_not_lose_the_marks() => Headless.Run(() =>
        {
            var f = Field(Doc3(), width: 520);
            while (!f.CheckSpellingStep()) { }
            Fixtures.Layout(f, 140);   // a narrower field lays every paragraph out again
            Assert.Single(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
            Assert.True(f.IsSpellingComplete);
            f.InvalidateAllLayouts();   // the styles changed: results may differ, so they are made again
            Assert.False(f.IsSpellingComplete);
        });

        [Fact]
        public void Only_the_paragraphs_in_view_are_checked_and_scrolling_makes_others_due() => Headless.Run(() =>
        {
            var doc = new Document();
            for (int i = 0; i < 40; i++) doc.Paragraphs.Add(TestDocs.Para("en", "en", "word" + i + " saat"));
            var field = Fixtures.Field(doc);
            double top = 0, bottom = 100;
            field.SpellIdlePost = a => { };
            field.SpellVisibleSpan = () => (top, bottom);
            field.SpellChecking = new FakeSpellHost(Enumerable.Range(0, 40).Select(i => "word" + i).ToArray());
            while (!field.CheckSpellingStep()) { }
            Assert.NotEmpty(field.SpellingMarks(0));
            Assert.Empty(field.SpellingMarks(39));
            Assert.Empty(field.SpellingMarks(20));
            Assert.True(field.IsSpellingComplete);
            top = field.ParagraphTop(20);
            bottom = top + 100;
            Assert.False(field.IsSpellingComplete);
            while (!field.CheckSpellingStep()) { }
            Assert.NotEmpty(field.SpellingMarks(20));
            Assert.NotEmpty(field.SpellingMarks(0));   // what was checked stays checked
            Assert.Empty(field.SpellingMarks(39));
        });

        [Fact]
        public void A_read_only_field_marks_nothing_unless_a_run_forces_checking() => Headless.Run(() =>
        {
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "saat ", "en", "teh"));
            doc.Paragraphs[0].Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckForce);
            var f = Field(doc);
            f.IsReadOnly = true;
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "teh" }, Words(f, 0));
            f.IsReadOnly = false;
            Assert.False(f.IsSpellingComplete);   // the rule changed, so the results are made again
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "saat", "teh" }, Words(f, 0));
        });

        [Fact]
        public void Suggestions_at_a_position_describe_the_marked_word_for_the_context_menu() => Headless.Run(() =>
        {
            var host = Host();
            host.Checkers["en"].Suggestions = new List<string> { "seat", "sat" };
            var f = Field(Doc3(), host);
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9));
            Assert.NotNull(s);
            Assert.Equal("saat", s!.Word);
            Assert.Equal(new[] { "seat", "sat" }, s.All);
            Assert.Equal("en", s.Ws);
            Assert.Null(f.SpellingSuggestionsAt(new TextPosition(0, 1)));
            f.SpellChecking = null;
            Assert.Null(f.SpellingSuggestionsAt(new TextPosition(0, 9)));
        });

        [Fact]
        public void Choosing_a_suggestion_replaces_the_word_keeps_its_formatting_and_undoes_as_one_step() => Headless.Run(() =>
        {
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "The cat ", "en", "saat", "en", " down"));
            doc.Paragraphs[0].Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            var host = Host();
            host.Checkers["en"].Suggestions = new List<string> { "seat" };
            var f = Field(doc, host);
            var undo = new SimpleUndoStack();
            undo.Attach(f.Editor);
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            Assert.True(f.ApplySpellingSuggestion(s, "seat"));
            Assert.Equal("The cat seat down", f.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(1, f.Editor.Document.Paragraphs[0].Runs.Single(r => r.Text == "seat").Props.IntProps[TextPropertyNumbers.Bold].Value);
            Assert.Empty(f.SpellingMarks(0));
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(Words(f, 0));
            Assert.Equal(1, undo.DoneSteps);
            Assert.True(undo.Undo());
            Assert.Equal("The cat saat down", f.Editor.Document.Paragraphs[0].Text);
        });

        [Fact]
        public void A_suggestion_is_refused_in_read_only_text_and_for_text_that_changed_since_the_menu_was_made() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Editor.TypeText("Zz ");
            Assert.False(f.ApplySpellingSuggestion(s, "seat"));
            Assert.Equal("Zz The cat saat down", f.Editor.Document.Paragraphs[0].Text);
            var s2 = f.SpellingSuggestionsAt(new TextPosition(0, 12))!;
            f.IsReadOnly = true;
            Assert.False(f.ApplySpellingSuggestion(s2, "seat"));
        });

        [Fact]
        public void Adding_to_the_dictionary_goes_to_the_host_and_the_word_stops_being_marked() => Headless.Run(() =>
        {
            var host = Host();
            var f = Field(Doc3(), host);
            while (!f.CheckSpellingStep()) { }
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            Assert.True(f.AddToDictionary(s));
            Assert.Equal(new[] { "saat" }, host.Checkers["en"].Added);
            Assert.False(f.IsSpellingComplete);
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(f.SpellingMarks(0));
            Assert.Equal(new[] { "teh" }, Words(f, 2));
        });

        [Fact]
        public void Ignoring_goes_to_the_host_and_a_word_that_is_not_a_misspelling_cannot_be_added() => Headless.Run(() =>
        {
            var host = Host().With("fr", "ok");
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "saat ab", "fr", "cd"));
            var f = Field(doc, host);
            while (!f.CheckSpellingStep()) { }
            Assert.True(f.IgnoreWord(f.SpellingSuggestionsAt(new TextPosition(0, 1))!));
            Assert.Equal(new[] { "saat" }, host.Checkers["en"].Ignored);
            var mixed = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            Assert.Equal(SpellingProblem.Mixed, mixed.Problem);
            Assert.False(f.AddToDictionary(mixed));
            Assert.False(f.IgnoreWord(mixed));
        });

        [Fact]
        public void Restarting_makes_every_paragraph_be_checked_again() => Headless.Run(() =>
        {
            var host = Host();
            var f = Field(Doc3(), host);
            while (!f.CheckSpellingStep()) { }
            host.Checkers["en"].Known.Add("saat");
            Assert.NotEmpty(f.SpellingMarks(0));
            f.RestartSpelling();
            Assert.Empty(f.SpellingMarks(0));
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(f.SpellingMarks(0));
        });

        [Fact]
        public void The_marks_do_not_change_how_the_squiggle_underline_property_is_drawn() => Headless.Run(() =>
        {
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "plain ", "en", "wavy"));
            doc.Paragraphs[0].Runs[1].Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSquiggle).SetInt(TextPropertyNumbers.UnderColor, 0x00FF00);
            var f = Field(doc, new FakeSpellHost("plain", "wavy"));
            var before = Fixtures.Render(f);
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(f.SpellingMarks(0));
            var after = Fixtures.Render(f);
            for (int x = 0; x < before.Width; x += 3)
                for (int y = 0; y < before.Height; y += 3)
                    Assert.Equal(before.HasColor(x, y), after.HasColor(x, y));
        });

        [Fact]
        public void While_an_input_method_composes_inside_a_marked_word_that_mark_is_not_drawn_and_the_others_are() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            f.Editor.SetCaret(new TextPosition(0, 9));
            f.SetPreedit("x", null);
            Assert.Empty(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
            f.SetPreedit(null, null);
            Assert.Single(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
        });
    }
}
