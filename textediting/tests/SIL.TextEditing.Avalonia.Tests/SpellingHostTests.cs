using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 95: what a host sees when spell checking fails, what it implements, and the edges around menus, ignoring and replacing.</summary>
    public class SpellingHostTests
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

        static void CheckAll(RichTextField f) { while (!f.CheckSpellingStep()) { } }

        static string[] Words(RichTextField f, int p) => f.SpellingMarks(p).Select(m => f.Editor.Document.Paragraphs[p].Text.Substring(m.Start, m.Length)).ToArray();

        // ---- a failure nobody handles is not silent

        [Fact]
        public void A_failure_nobody_handles_is_traced_once_per_writing_system_until_a_restart() => Headless.Run(() =>
        {
            var host = Host().With("fr", "bon");
            host.Checkers["en"].Throws = new InvalidOperationException("dictionary gone");
            host.Checkers["fr"].Throws = new InvalidOperationException("french gone");
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", "xx yy ", "fr", "mauvais zz")), host);
            var traced = new List<string>();
            f.SpellTraceSink = traced.Add;
            CheckAll(f);
            Assert.Equal(2, traced.Count);
            Assert.Contains("'en'", traced.Single(t => t.Contains("dictionary gone")));
            Assert.Contains("'fr'", traced.Single(t => t.Contains("french gone")));
            CheckAll(f);
            f.CheckSpellingStep();
            Assert.Equal(2, traced.Count);
            f.RestartSpelling();
            CheckAll(f);
            Assert.Equal(4, traced.Count);
        });

        [Fact]
        public void A_failure_that_a_handler_takes_is_not_traced_and_a_handler_that_throws_is_traced_instead_of_thrown() => Headless.Run(() =>
        {
            var host = Host();
            host.Checkers["en"].Throws = new InvalidOperationException("boom");
            var f = Field(Doc3(), host);
            var traced = new List<string>();
            f.SpellTraceSink = traced.Add;
            int handled = 0;
            f.SpellCheckerFailed += (ws, e) => handled++;
            CheckAll(f);
            Assert.Equal(1, handled);
            Assert.Empty(traced);

            var f2 = Field(Doc3(), host);
            var traced2 = new List<string>();
            f2.SpellTraceSink = traced2.Add;
            f2.SpellCheckerFailed += (ws, e) => throw new ApplicationException("handler bug");
            CheckAll(f2);   // does not throw
            Assert.Single(traced2);
            Assert.Contains("handler bug", traced2[0]);
        });

        [Fact]
        public void A_host_whose_GetChecker_throws_is_reported_once_and_nothing_is_marked() => Headless.Run(() =>
        {
            var host = Host();
            host.GetCheckerThrows = new InvalidOperationException("no checker");
            var (window, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = a => { };
            f.SpellChecking = host;
            var failures = new List<(string, Exception)>();
            f.SpellCheckerFailed += (ws, e) => failures.Add((ws, e));
            CheckAll(f);
            Assert.Single(failures);
            Assert.Equal("en", failures[0].Item1);
            Assert.Equal("no checker", failures[0].Item2.Message);
            Assert.All(Enumerable.Range(0, 3), i => Assert.Empty(f.SpellingMarks(i)));
            host.GetCheckerThrows = null;
            host.RaiseDictionariesChanged();
            Headless.Pump();
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));   // repaired, and the host's notice made the field try again
            window.Close();
        });

        [Fact]
        public void A_host_whose_JoinsWords_throws_is_reported_and_the_default_rule_is_used() => Headless.Run(() =>
        {
            var host = Host();
            host.JoinsWordsThrows = new InvalidOperationException("no joiners");
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", "The don't cat")), host);
            host.Checkers["en"].Known.Add("don't");
            var failed = new List<string>();
            f.SpellCheckerFailed += (ws, e) => failed.Add(ws);
            CheckAll(f);
            Assert.Equal(new[] { "en" }, failed);
            Assert.Empty(Words(f, 0));   // the default rule keeps "don't" one word
        });

        [Fact]
        public void A_checker_whose_Suggest_throws_marks_the_word_and_offers_no_suggestions_and_is_reported() => Headless.Run(() =>
        {
            var host = Host();
            host.Checkers["en"].SuggestThrows = new InvalidOperationException("no suggestions today");
            var f = Field(Doc3(), host);
            var failures = new List<string>();
            f.SpellCheckerFailed += (ws, e) => failures.Add(e.Message);
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            Assert.Empty(s.All);
            Assert.Equal(new[] { "no suggestions today" }, failures);
        });

        // ---- one wrapper per checker, not one per word

        [Fact]
        public void The_field_makes_one_wrapper_per_checker_until_the_host_changes_it_or_checking_restarts() => Headless.Run(() =>
        {
            var host = Host();
            var f = Field(Doc3(), host);
            CheckAll(f);
            Assert.Equal(1, f.SpellWrappersCreated);
            f.SpellingSuggestionsAt(new TextPosition(0, 9));
            Assert.Equal(1, f.SpellWrappersCreated);
            var replacement = new FakeChecker();
            replacement.Known.Add("saat");
            host.Checkers["en"] = replacement;      // the host swaps the checker: the field must notice
            f.RestartSpelling();
            CheckAll(f);
            Assert.Equal(2, f.SpellWrappersCreated);
            Assert.DoesNotContain("saat", Words(f, 0));
            host.Checkers["en"] = new FakeChecker();
            f.CheckSpellingStep();
            Assert.Equal(2, f.SpellWrappersCreated);
            f.SpellingSuggestionsAt(new TextPosition(0, 9));   // asked again: a different checker, a new wrapper
            Assert.Equal(3, f.SpellWrappersCreated);
        });

        // ---- ignoring, the guard, and the host

        [Fact]
        public void A_checker_that_cannot_ignore_stays_one_that_cannot_ignore_through_the_guard() => Headless.Run(() =>
        {
            var host = Host();
            var bare = new FakeBareChecker();
            host.Custom["en"] = bare;
            var f = Field(Doc3(), host);
            CheckAll(f);
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 9))!;
            Assert.True(s.CanAddToDictionary);
            Assert.False(s.CanIgnore);
            Assert.False(f.IgnoreWord(s));
            Assert.True(f.IsSpellingComplete);   // nothing was restarted
            host.Custom.Clear();
            var f2 = Field(Doc3(), host);
            CheckAll(f2);
            Assert.True(f2.SpellingSuggestionsAt(new TextPosition(0, 9))!.CanIgnore);
        });

        [Fact]
        public void Ignoring_a_word_checks_again_so_its_squiggle_goes() => Headless.Run(() =>
        {
            var host = Host();
            var f = Field(Doc3(), host);
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            Assert.True(f.IgnoreWord(f.SpellingSuggestionsAt(new TextPosition(0, 9))!));
            Assert.False(f.IsSpellingComplete);
            Assert.Empty(f.SpellingMarks(0));
            CheckAll(f);
            Assert.Empty(Words(f, 0));
            Assert.Equal(new[] { "teh" }, Words(f, 2));
        });

        [Fact]
        public void Replacing_the_host_stops_listening_to_the_old_one_and_listens_to_the_new() => Headless.Run(() =>
        {
            var one = Host();
            var two = Host();
            var (window, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = x => { };
            f.SpellChecking = one;
            Assert.Equal(1, one.Subscribers);
            f.SpellChecking = two;
            Assert.Equal(0, one.Subscribers);
            Assert.Equal(1, two.Subscribers);
            f.SpellChecking = null;
            Assert.Equal(0, two.Subscribers);
            window.Close();
        });

        [Fact]
        public void A_replacement_is_refused_in_text_whose_run_may_not_be_edited_even_when_it_is_checked_anyway() => Headless.Run(() =>
        {
            var p = TestDocs.Para("en", "en", "The ", "en", "saat", "en", " down");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckForce);
            var f = Field(TestDocs.Doc(p));
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            var s = f.SpellingSuggestionsAt(new TextPosition(0, 5))!;
            Assert.False(f.ApplySpellingSuggestion(s, "seat"));
            Assert.False(f.ApplySpellingSuggestion(s, new List<Run> { new Run { Ws = "en", Text = "seat" } }));
            Assert.Equal("The saat down", f.Editor.Document.Paragraphs[0].Text);
        });

        // ---- the context request: the right-click path and the space beside a word

        static Point CenterOf(Window window, RichTextField field, int start, int end)
        {
            var r = field.ParagraphLayoutFor(0).SelectionRects(start, end)[0];
            return field.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), window)!.Value;
        }

        [Fact]
        public void A_right_click_on_a_misspelled_word_carries_the_spelling_problem_and_one_on_the_space_beside_it_carries_none() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(TestDocs.OneParagraph("hello wrold again"));
            var host = new FakeSpellHost("hello", "again");
            host.Checkers["en"].Suggestions = new List<string> { "world", "wold" };
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);

            var on = CenterOf(window, field, 7, 8);                  // the "o" of "wrold"
            window.MouseDown(on, MouseButton.Right);
            window.MouseUp(on, MouseButton.Right);
            Headless.Pump();
            Assert.Single(seen);
            Assert.Equal(ContextMenuTrigger.Pointer, seen[0].Trigger);
            Assert.Equal("wrold", seen[0].Info.Word);
            Assert.NotNull(seen[0].Info.Spelling);
            Assert.Equal("wrold", seen[0].Info.Spelling!.Word);
            Assert.Equal(new[] { "world", "wold" }, seen[0].Info.SpellingSuggestions);

            var space = CenterOf(window, field, 11, 12);             // the space right after "wrold"
            window.MouseDown(space, MouseButton.Right);
            window.MouseUp(space, MouseButton.Right);
            Headless.Pump();
            Assert.Equal(2, seen.Count);
            Assert.Equal(string.Empty, seen[1].Info.Word);
            Assert.Null(seen[1].Info.Spelling);
            Assert.Empty(seen[1].Info.SpellingSuggestions);
            window.Close();
        });
    }
}
