using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Spell checking at the edges: crashes, hidden text, the tree, notifications, replacements that keep writing systems.</summary>
    public class SpellingFieldFixTests
    {
        static FakeSpellHost Host() => new FakeSpellHost("The", "cat", "down", "all", "good", "here", "and", "end", "the", "seat", "sat");

        static Document Doc3() => TestDocs.Doc(
            TestDocs.Para("en", "en", "The cat saat down"),
            TestDocs.Para("en", "en", "all good here"),
            TestDocs.Para("en", "en", "and teh end"));

        static RichTextField Field(Document doc, ISpellCheckHost? host = null)
        {
            var field = Fixtures.Field(doc);
            field.SpellIdlePost = a => { };
            field.SpellChecking = host ?? Host();
            return field;
        }

        static string[] Words(RichTextField f, int p) => f.SpellingMarks(p).Select(m => f.Editor.Document.Paragraphs[p].Text.Substring(m.Start, m.Length)).ToArray();

        // ---- a checker that throws must not take the dispatcher down (P3-14)

        [Fact]
        public void A_checker_that_throws_is_reported_once_and_that_writing_system_is_not_checked_again() => Headless.Run(() =>
        {
            var host = Host();
            host.Checkers["en"].Throws = new InvalidOperationException("dictionary gone");
            var f = Field(Doc3(), host);
            var failures = new List<(string, Exception)>();
            f.SpellCheckerFailed += (ws, ex) => failures.Add((ws, ex));
            while (!f.CheckSpellingStep()) { }   // does not throw
            Assert.Single(failures);
            Assert.Equal("en", failures[0].Item1);
            Assert.Equal("dictionary gone", failures[0].Item2.Message);
            Assert.Single(host.Checkers["en"].Asked);   // after the first failure the writing system is left alone
            Assert.All(Enumerable.Range(0, 3), i => Assert.Empty(f.SpellingMarks(i)));
            Assert.True(f.IsSpellingComplete);
        });

        [Fact]
        public void Restarting_gives_a_failed_writing_system_another_chance_and_other_writing_systems_go_on()
        {
            Headless.Run(() =>
            {
                var host = Host().With("fr", "bon");
                host.Checkers["en"].Throws = new InvalidOperationException("boom");
                var doc = TestDocs.Doc(TestDocs.Para("en", "en", "xx ", "fr", "mauvais"));
                var f = Field(doc, host);
                int failures = 0;
                f.SpellCheckerFailed += (ws, ex) => failures++;
                while (!f.CheckSpellingStep()) { }
                Assert.Equal(1, failures);
                Assert.Equal(new[] { "mauvais" }, Words(f, 0));   // French was still checked
                host.Checkers["en"].Throws = null;
                f.RestartSpelling();
                while (!f.CheckSpellingStep()) { }
                Assert.Equal(1, failures);
                Assert.Equal(new[] { "xx", "mauvais" }, Words(f, 0));
            });
        }

        [Fact]
        public void A_suggestion_request_whose_checker_throws_gives_no_suggestions_instead_of_an_exception() => Headless.Run(() =>
        {
            var host = Host();
            var f = Field(Doc3(), host);
            host.Checkers["en"].Suggestions = new List<string> { "seat" };
            Assert.Equal(new[] { "seat" }, f.SpellingSuggestionsAt(new TextPosition(0, 9))!.All);
            host.Checkers["en"].Throws = new InvalidOperationException("boom");
            f.RestartSpelling();   // the host changed what its checker answers, so the field forgets earlier answers
            // the word cannot even be judged now: nothing is marked there, and nothing is thrown
            Assert.Null(f.SpellingSuggestionsAt(new TextPosition(0, 9)));
        });

        [Fact]
        public void A_host_that_throws_for_word_forming_is_reported_and_the_default_rule_is_used() => Headless.Run(() =>
        {
            var host = Host();
            host.WordFormingThrows = new InvalidOperationException("no ws data");
            var f = Field(Doc3(), host);
            var failed = new List<string>();
            f.SpellCheckerFailed += (ws, ex) => failed.Add(ws);
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "en" }, failed);
        });

        // ---- only in the visual tree, and never the whole of a long document (P3-7)

        static Document Long(int n)
        {
            var doc = new Document();
            for (int i = 0; i < n; i++) doc.Paragraphs.Add(TestDocs.Para("en", "en", "wrong" + i));
            return doc;
        }

        [Fact]
        public void A_field_that_is_not_in_the_tree_asks_for_no_idle_time_until_it_is() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var f = Fixtures.Field(Doc3());
            f.SpellIdlePost = posts.Add;
            f.SpellChecking = Host();
            Assert.Empty(posts);
            var window = new Window { Width = 520, Height = 400, Content = f };
            window.Show();
            Headless.Pump();
            Assert.Single(posts);
            posts[0]();
            window.Content = null;
            Headless.Pump();
            posts.Clear();
            f.RestartSpelling();
            Assert.Empty(posts);   // detached again
            window.Close();
        });

        [Fact]
        public void An_idle_step_that_runs_after_the_field_left_the_tree_checks_nothing() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var (window, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = posts.Add;
            f.SpellChecking = Host();
            window.Content = null;
            Headless.Pump();
            posts[0]();
            Assert.Empty(f.SpellingMarks(0));
            window.Close();
        });

        [Fact]
        public void A_field_that_is_in_no_scroll_viewer_checks_what_fits_the_window_not_the_document() => Headless.Run(() =>
        {
            var doc = Long(400);
            var field = new RichTextField(doc) { SpellIdlePost = a => { } };
            var window = new Window { Width = 300, Height = 200, Content = field };
            window.Show();
            Headless.Pump();
            field.SpellChecking = new FakeSpellHost();
            while (!field.CheckSpellingStep()) { }
            Assert.NotEmpty(field.SpellingMarks(0));
            Assert.Empty(field.SpellingMarks(399));
            Assert.Empty(field.SpellingMarks(100));
            window.Close();
        });

        [Fact]
        public void A_field_with_nothing_to_measure_against_is_bounded_too() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Long(400));
            f.SpellIdlePost = a => { };
            f.SpellChecking = new FakeSpellHost();
            while (!f.CheckSpellingStep()) { }
            Assert.NotEmpty(f.SpellingMarks(0));
            Assert.Empty(f.SpellingMarks(399));
        });

        [Fact]
        public void The_edges_of_the_visible_span_count_as_visible()
        {
            Headless.Run(() =>
            {
                var doc = Long(8);
                var f = Fixtures.Field(doc);
                f.SpellIdlePost = a => { };
                double h0 = f.ParagraphLayoutFor(0).Height;
                double top = h0;                       // paragraph 0 ends exactly where the span starts
                double bottom = f.ParagraphTop(3);     // paragraph 3 starts exactly where it ends
                f.SpellVisibleSpan = () => (top, bottom);
                f.SpellChecking = new FakeSpellHost();
                while (!f.CheckSpellingStep()) { }
                Assert.NotEmpty(f.SpellingMarks(0));
                Assert.NotEmpty(f.SpellingMarks(1));
                Assert.NotEmpty(f.SpellingMarks(2));
                Assert.NotEmpty(f.SpellingMarks(3));
                Assert.Empty(f.SpellingMarks(4));
                top = f.ParagraphTop(1) + 0.5;         // now paragraph 0 ends above the span
                f.RestartSpelling();
                while (!f.CheckSpellingStep()) { }
                Assert.Empty(f.SpellingMarks(0));
                Assert.NotEmpty(f.SpellingMarks(1));
            });
        }

        // ---- the host says the dictionaries changed (P3-6)

        [Fact]
        public void A_dictionaries_changed_notice_checks_every_field_of_the_host_again_and_detaching_stops_listening() => Headless.Run(() =>
        {
            var host = Host();
            var (w1, a) = Fixtures.InWindow(Doc3());
            var (w2, b) = Fixtures.InWindow(Doc3());
            a.SpellIdlePost = x => { };
            b.SpellIdlePost = x => { };
            a.SpellChecking = host;
            b.SpellChecking = host;
            Assert.Equal(2, host.Subscribers);
            while (!a.CheckSpellingStep()) { }
            while (!b.CheckSpellingStep()) { }
            Assert.Equal(new[] { "saat" }, Words(a, 0));
            host.Checkers["en"].Known.Add("saat");
            host.RaiseDictionariesChanged();
            Assert.Empty(a.SpellingMarks(0));
            Assert.Empty(b.SpellingMarks(0));
            while (!a.CheckSpellingStep()) { }
            Assert.Empty(Words(a, 0));
            a.SpellChecking = Host();
            Assert.Equal(1, host.Subscribers);   // replaced: no longer listening to the old host
            w2.Content = null;
            Headless.Pump();
            Assert.Equal(0, host.Subscribers);   // out of the tree: no longer listening
            w2.Content = b;
            Headless.Pump();
            Assert.Equal(1, host.Subscribers);   // back: listening again
            w1.Close();
            w2.Close();
        });

        [Fact]
        public void A_notice_from_another_thread_is_handled_on_the_fields_thread() => Headless.Run(async () =>
        {
            var host = Host();
            var (w, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = x => { };
            f.SpellChecking = host;
            while (!f.CheckSpellingStep()) { }
            await System.Threading.Tasks.Task.Run(() => host.RaiseDictionariesChanged());
            Headless.Pump();
            Assert.Empty(f.SpellingMarks(0));
            w.Close();
        });

        // ---- text a line limit hides (P3-9)

        static (RichTextField Field, int Hidden) Limited()
        {
            var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => "wrd" + i));
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", words));
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var f = Fixtures.Field(doc, 200);
            f.SpellIdlePost = a => { };
            f.SpellChecking = new FakeSpellHost();
            return (f, f.ParagraphLayoutFor(0).VisibleEnd);
        }

        [Fact]
        public void Text_the_line_limit_hides_is_not_checked_not_offered_a_menu_and_not_replaced() => Headless.Run(() =>
        {
            var (f, visible) = Limited();
            Assert.InRange(visible, 5, 100);
            while (!f.CheckSpellingStep()) { }
            var marks = f.SpellingMarks(0);
            Assert.NotEmpty(marks);
            Assert.All(marks, m => Assert.True(m.Start < visible));
            var host = (FakeSpellHost)f.SpellChecking!;
            Assert.True(host.Checkers["en"].Asked.Count < 30);
            Assert.Null(f.SpellingSuggestionsAt(new TextPosition(0, f.Editor.Document.Paragraphs[0].Length - 2)));
            var shown = f.SpellingSuggestionsAt(new TextPosition(0, 1))!;
            Assert.NotNull(shown);
            // a menu made before the field narrowed, for text now hidden, cannot be applied
            var hidden = SpellScanner.SuggestionsAt(f.Editor.Document.Paragraphs[0], 0, f.Editor.Document.Paragraphs[0].Length - 2, host)!;
            Assert.False(f.ApplySpellingSuggestion(hidden, "x"));
        });

        [Fact]
        public void Widening_a_field_checks_text_that_became_visible() => Headless.Run(() =>
        {
            var (f, visible) = Limited();
            while (!f.CheckSpellingStep()) { }
            int before = f.SpellingMarks(0).Count;
            Fixtures.Layout(f, 2000);
            Assert.False(f.IsSpellingComplete);
            while (!f.CheckSpellingStep()) { }
            Assert.True(f.SpellingMarks(0).Count > before);
        });

        // ---- replacing a word that is not a plain misspelling (P2-2)

        [Fact]
        public void Inserting_the_missing_spaces_in_a_mixed_word_keeps_each_part_in_its_own_writing_system() => Headless.Run(() =>
        {
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "see ", "en", "ab", "fr", "cd", "en", " end"));
            doc.Paragraphs[0].Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            var f = Field(doc, new FakeSpellHost("see", "ab", "end").With("fr", "cd"));
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 5))!;
            Assert.Equal(SpellingProblem.Mixed, s.Problem);
            Assert.True(f.ApplySpellingSuggestion(s, s.WithMissingSpaces()));
            var runs = f.Editor.Document.Paragraphs[0].Runs;
            Assert.Equal("see ab cd end", f.Editor.Document.Paragraphs[0].Text);
            Assert.Equal("fr", runs.Single(r => r.Text == "cd").Ws);
            Assert.Equal("en", runs.First(r => r.Text.Contains("ab")).Ws);
            Assert.Equal(1, runs.First(r => r.Text.Contains("ab")).Props.IntProps[TextPropertyNumbers.Bold].Value);
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(f.SpellingMarks(0));
        });

        [Fact]
        public void Inserting_the_missing_spaces_around_an_object_keeps_the_object() => Headless.Run(() =>
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var f = Field(TestDocs.Doc(p), new FakeSpellHost("ab", "cd"));
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 1))!;
            Assert.Equal(SpellingProblem.Embedded, s.Problem);
            Assert.True(f.ApplySpellingSuggestion(s, s.WithMissingSpaces()));
            var para = f.Editor.Document.Paragraphs[0];
            Assert.Equal("ab ￼ cd", para.Text);
            Assert.Equal("pic", para.Runs.Single(r => r.IsObject).ObjData);
            while (!f.CheckSpellingStep()) { }
            Assert.Empty(f.SpellingMarks(0));
        });

        [Fact]
        public void A_replacement_string_cannot_destroy_an_object_but_may_make_a_mixed_word_one_writing_system() => Headless.Run(() =>
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            var f = Field(TestDocs.Doc(p), new FakeSpellHost());
            Assert.False(f.ApplySpellingSuggestion(f.SpellingSuggestionsAt(new TextPosition(0, 1))!, "ab"));
            Assert.Contains(f.Editor.Document.Paragraphs[0].Runs, r => r.IsObject);
            var mixed = Field(TestDocs.Doc(TestDocs.Para("en", "en", "ab", "fr", "cd")), new FakeSpellHost());
            Assert.True(mixed.ApplySpellingSuggestion(mixed.SpellingSuggestionsAt(new TextPosition(0, 1))!, "abcd"));
            Assert.Equal("en", mixed.Editor.Document.Paragraphs[0].Runs.Single().Ws);
        });

        [Fact]
        public void An_empty_replacement_list_or_one_for_changed_text_is_refused() => Headless.Run(() =>
        {
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", "ab", "fr", "cd")), new FakeSpellHost());
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 1))!;
            Assert.False(f.ApplySpellingSuggestion(s, new List<Run>()));
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Editor.TypeText("z");
            Assert.False(f.ApplySpellingSuggestion(s, s.WithMissingSpaces()));
        });

        [Fact]
        public void A_replacement_ends_the_typing_step_before_it_so_it_is_a_step_of_its_own() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            var undo = new SimpleUndoStack();
            undo.Attach(f.Editor);
            f.Editor.SetCaret(new TextPosition(1, 0));
            f.Editor.TypeText("x");   // an open typing step
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            Assert.True(f.ApplySpellingSuggestion(s, "seat"));
            Assert.Equal(2, undo.DoneSteps);
        });

        // ---- drawing

        [Fact]
        public void An_embedded_word_is_drawn_red_and_a_mixed_one_blue() => Headless.Run(() =>
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = " ok " });
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            p.Runs.Add(new Run { Ws = "fr", Text = "ef" });
            var f = Field(TestDocs.Doc(p), new FakeSpellHost("ok"));
            while (!f.CheckSpellingStep()) { }
            var drawn = f.ParagraphLayoutFor(0).SpellingMarksDrawn;
            Assert.Equal(2, drawn.Count);
            Assert.Equal(Colors.Red, ((ISolidColorBrush)drawn[0].Brush).Color);
            Assert.Equal(Colors.Blue, ((ISolidColorBrush)drawn[1].Brush).Color);
        });

        [Fact]
        public void A_mark_that_starts_where_the_composing_text_is_inserted_is_drawn_after_it() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            f.Editor.SetCaret(new TextPosition(0, 8));   // "saat" starts here
            f.SetPreedit("xyz", null);
            var drawn = f.ParagraphLayoutFor(0).SpellingMarksDrawn;
            Assert.Single(drawn);
            Assert.Equal((11, 15), (drawn[0].Start, drawn[0].End));
            f.SetPreedit(null, null);
        });

        [Fact]
        public void A_mark_that_ends_where_the_composing_text_is_inserted_stays_before_it() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            f.Editor.SetCaret(new TextPosition(0, 12));   // "saat" ends here
            f.SetPreedit("xyz", null);
            var drawn = f.ParagraphLayoutFor(0).SpellingMarksDrawn;
            Assert.Equal((8, 12), (drawn[0].Start, drawn[0].End));
            f.SetPreedit(null, null);
        });

        // ---- guards that nothing else pins

        [Fact]
        public void A_result_for_text_that_has_changed_behind_the_editor_is_not_used() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            while (!f.CheckSpellingStep()) { }
            Assert.NotEmpty(f.SpellingMarks(0));
            f.Editor.Document.Paragraphs[0].Runs[0].Text = "Another saat sentence";   // no DocumentChanged
            Assert.Empty(f.SpellingMarks(0));
            Assert.False(f.IsSpellingComplete);
        });

        sealed class CountingField : RichTextField
        {
            public CountingField(Document d) : base(d) { }
            public int Renders;
            public override void Render(DrawingContext context) { Renders++; base.Render(context); }
        }

        [Fact]
        public void A_checking_step_asks_for_a_repaint() => Headless.Run(() =>
        {
            var f = new CountingField(Doc3());
            var window = new Window { Width = 520, Height = 400, Content = f };
            window.Show();
            Headless.Pump();
            f.SpellIdlePost = a => { };
            f.SpellChecking = Host();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            int before = f.Renders;
            f.CheckSpellingStep();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            Assert.True(f.Renders > before);
            window.Close();
        });
    }
}
