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
    /// <summary>Issue 93: a context request names the word by the spell checker's rule, a mixed word can be made one writing system, and drawing
    /// a field does not rebuild its paragraphs' text.</summary>
    public class SpellingContextTests
    {
        static ContextInfo Request(RichTextField field, int offset)
        {
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            field.Editor.SetCaret(new TextPosition(0, offset));
            field.Focus();
            Headless.Pump();
            field.Press(Key.Apps);
            Assert.Single(seen);
            return seen[0].Info;
        }

        static Point CenterOf(Window window, RichTextField field, int start, int end)
        {
            var r = field.ParagraphLayoutFor(0).SelectionRects(start, end)[0];
            return field.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), window)!.Value;
        }

        static ContextInfo Click(Window window, RichTextField field, int start, int end)
        {
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            var at = CenterOf(window, field, start, end);
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Headless.Pump();
            Assert.Single(seen);
            return seen[0].Info;
        }

        [Fact]
        public void The_word_of_a_context_request_is_the_marked_word_and_an_underscore_ends_both() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(TestDocs.OneParagraph("foo_bar baz"));
            var host = new FakeSpellHost("foo", "baz");
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            while (!field.CheckSpellingStep()) { }
            Assert.Equal(new[] { new SpellingMark(4, 3, SpellingProblem.Misspelled) }, field.SpellingMarks(0));
            var info = Request(field, 5);
            Assert.Equal("bar", info.Word);
            Assert.Equal(new TextRange(0, 4, 7), info.WordRange);
            Assert.NotNull(info.Spelling);
            Assert.Equal("bar", info.Spelling!.Word);
            Assert.Equal((4, 7), (info.Spelling.Start.Offset, info.Spelling.End.Offset));
            window.Close();
        });

        [Fact]
        public void A_host_that_makes_the_underscore_a_letter_changes_the_word_of_the_marks_and_of_the_request_together() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(TestDocs.OneParagraph("foo_bar baz"));
            var host = new FakeSpellHost("foo", "baz");
            host.WordForming.Add('_');
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            while (!field.CheckSpellingStep()) { }
            Assert.Equal(new[] { new SpellingMark(0, 7, SpellingProblem.Misspelled) }, field.SpellingMarks(0));
            var info = Request(field, 5);
            Assert.Equal("foo_bar", info.Word);
            Assert.Equal(new TextRange(0, 0, 7), info.WordRange);
            Assert.Equal("foo_bar", info.Spelling!.Word);
            window.Close();
        });

        [Fact]
        public void Without_a_spell_checker_the_word_has_the_same_defaults_so_an_underscore_ends_it_there_too() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(TestDocs.OneParagraph("foo_bar baz"));
            var info = Request(field, 5);
            Assert.Equal("bar", info.Word);
            Assert.Null(info.Spelling);
            window.Close();
        });

        static Paragraph WithObject()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd ok" });
            return p;
        }

        [Fact]
        public void A_word_beside_an_object_is_the_text_of_the_request_and_the_marked_word_goes_through_the_object() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(TestDocs.Doc(WithObject()));
            var host = new FakeSpellHost("ok");
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            while (!field.CheckSpellingStep()) { }
            var info = Click(window, field, 3, 4);                    // the "c" of "cd"
            Assert.Equal("cd", info.Word);
            Assert.Equal(new TextRange(0, 3, 5), info.WordRange);
            Assert.NotNull(info.Spelling);
            Assert.Equal(SpellingProblem.Embedded, info.Spelling!.Problem);
            Assert.Equal((0, 5), (info.Spelling.Start.Offset, info.Spelling.End.Offset));   // "ab", the object and "cd"
            Assert.Contains(Run.ObjectReplacement.ToString(), info.Spelling.Word);
            window.Close();
        });

        [Fact]
        public void A_request_on_the_object_alone_has_no_word_and_so_no_spelling() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(TestDocs.Doc(WithObject()));
            var host = new FakeSpellHost("ok");
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            while (!field.CheckSpellingStep()) { }
            var info = Click(window, field, 2, 3);
            Assert.Equal(string.Empty, info.Word);
            Assert.NotNull(info.EmbeddedObject);
            Assert.Null(info.Spelling);
            window.Close();
        });

        // ---- make the whole word writing system X

        static (RichTextField Field, FakeSpellHost Host) MixedField()
        {
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", "see ", "en", "Glued", "fr", "mots", "en", " end"));
            var host = new FakeSpellHost("see", "end").With("fr", "mots");
            var field = Fixtures.Field(doc);
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            while (!field.CheckSpellingStep()) { }
            return (field, host);
        }

        [Fact]
        public void Making_a_mixed_word_one_writing_system_changes_only_the_writing_system_in_one_undo_step() => Headless.Run(() =>
        {
            var (field, host) = MixedField();
            var undo = new SimpleUndoStack();
            undo.Attach(field.Editor);
            var s = field.SpellingSuggestionsAt(new TextPosition(0, 6))!;
            Assert.Equal(SpellingProblem.Mixed, s.Problem);
            Assert.Equal(new[] { "en", "fr" }, s.WritingSystems);
            Assert.True(field.ApplySpellingSuggestion(s, s.WithWholeWordIn("fr")));
            var p = field.Editor.Document.Paragraphs[0];
            Assert.Equal("see Gluedmots end", p.Text);
            Assert.Equal("fr", p.RunAfter(5, out _)!.Ws);
            Assert.Equal("fr", p.RunAfter(11, out _)!.Ws);
            Assert.Equal(new[] { "en", "fr", "en" }, p.Runs.Select(r => r.Ws));
            Assert.Equal(1, undo.DoneSteps);
            while (!field.CheckSpellingStep()) { }
            var now = field.SpellingSuggestionsAt(new TextPosition(0, 6))!;
            Assert.Equal(SpellingProblem.Misspelled, now.Problem);       // one word in one writing system: the French dictionary is asked
            Assert.Equal("fr", now.Ws);
            undo.Undo();
            Assert.Equal(new[] { "en", "en", "fr", "en" }, field.Editor.Document.Paragraphs[0].Runs.Select(r => r.Ws));
            Assert.Equal("Glued", field.Editor.Document.Paragraphs[0].RunAfter(5, out _)!.Text);
            Assert.Equal("en", field.Editor.Document.Paragraphs[0].RunAfter(5, out _)!.Ws);
        });

        [Fact]
        public void Making_the_word_the_first_writing_system_works_too_and_a_word_in_one_writing_system_lists_only_that_one() => Headless.Run(() =>
        {
            var (field, host) = MixedField();
            var s = field.SpellingSuggestionsAt(new TextPosition(0, 6))!;
            Assert.True(field.ApplySpellingSuggestion(s, s.WithWholeWordIn("en")));
            Assert.Equal(new[] { "en" }, field.Editor.Document.Paragraphs[0].Runs.Select(r => r.Ws));
            while (!field.CheckSpellingStep()) { }
            var now = field.SpellingSuggestionsAt(new TextPosition(0, 6))!;
            Assert.Equal(new[] { "en" }, now.WritingSystems);
        });

        // ---- drawing does not rebuild the paragraphs' text

#if TEXTEDITING_COUNTERS
        [Fact]
        public void Drawing_a_field_with_marks_in_several_runs_does_not_read_the_text_of_its_paragraphs_again() => Headless.Run(() =>
        {
            var doc = new Document();
            for (int i = 0; i < 6; i++) doc.Paragraphs.Add(TestDocs.Para("en", "en", "The cat ", "fr", "saat", "en", " and teh end" + i));
            var host = new FakeSpellHost("The", "cat", "and", "end0", "end1", "end2", "end3", "end4", "end5").With("fr", "saat");
            var (window, field) = Fixtures.InWindow(doc);
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            while (!field.CheckSpellingStep()) { }
            Assert.NotEmpty(field.SpellingMarks(0));
            Fixtures.Render(field);                      // the first draw may build what it needs
            long before = doc.Paragraphs.Sum(p => p.TextReads);
            for (int i = 0; i < 20; i++) Fixtures.Render(field);
            Assert.Equal(before, doc.Paragraphs.Sum(p => p.TextReads));
            window.Close();
        });
#endif
    }
}
