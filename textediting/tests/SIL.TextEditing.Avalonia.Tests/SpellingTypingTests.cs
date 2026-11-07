using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 91: no flicker while typing (marks outside the edit stay and move), only the words around an edit are checked again, an idle step
    /// is bounded by words, and a hover costs one word.</summary>
    public class SpellingTypingTests
    {
        static string[] Known = { "the", "cat", "sat", "on", "a", "mat", "and", "dog", "ran", "to", "it", "is", "good", "here", "this", "that" };

        static string Sentence(int words, string wrong = "zzq")
        {
            // every fourth word is misspelled, so a long paragraph holds many marks
            return string.Join(" ", Enumerable.Range(0, words).Select(i => i % 4 == 3 ? wrong + i : Known[i % Known.Length]));
        }

        static RichTextField Field(Document doc, ISpellCheckHost host, double width = 520)
        {
            var field = Fixtures.Field(doc, width);
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            return field;
        }

        static void CheckAll(RichTextField f) { while (!f.CheckSpellingStep()) { } }

        static List<SpellingMark> Reference(Paragraph p, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false)
            => ReferenceSpellScanner.Check(p, host, styles, readOnly);

        // ---- marks outside the edit stay and move with the text

        [Fact]
        public void Typing_in_the_middle_of_a_paragraph_never_clears_the_squiggles_of_the_other_words() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", Sentence(60)));
            var f = Field(doc, host);
            CheckAll(f);
            var before = f.SpellingMarks(0).ToList();
            Assert.Equal(15, before.Count);
            string text = f.Editor.Document.Paragraphs[0].Text;
            int at = text.IndexOf("zzq31", StringComparison.Ordinal) + 5;   // just after a misspelled word, inside the paragraph
            f.Editor.SetCaret(new TextPosition(0, at));
            var typed = "x yz";
            var seenMarks = new List<int>();
            foreach (char c in typed)
            {
                f.Editor.TypeText(c.ToString());
                // at once, with no step run and before any drawing: every other word is still marked, and all that is marked is still wrong
                var now = f.SpellingMarks(0);
                var reference = Reference(f.Editor.Document.Paragraphs[0], host);
                Assert.All(now, m => Assert.Contains(m, reference));
                Assert.True(now.Count >= before.Count - 1, now.Count + " marks left after typing " + c);
                seenMarks.Add(now.Count);
                // the layout the next draw uses carries the same marks
                Assert.Equal(now.Count, f.ParagraphLayoutFor(0).SpellingMarksDrawn.Count);
                CheckAll(f);
                Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
            }
            Assert.All(seenMarks, n => Assert.True(n >= 14));
        });

        [Fact]
        public void Only_the_words_around_an_edit_are_checked_again_not_the_paragraph() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(400))), host);
            CheckAll(f);
            host.ResetCounts();
            string text = f.Editor.Document.Paragraphs[0].Text;
            f.Editor.SetCaret(new TextPosition(0, text.Length / 2));
            f.Editor.TypeText("q");
            while (!f.CheckSpellingStep()) { }
            // one word changed and its neighbours may have: a handful of dictionary lookups and a few hundred characters of word-forming questions
            Assert.True(host.English.Asked.Count <= 4, host.English.Asked.Count + " words asked after one keystroke in 400 words");
            Assert.True(host.WordFormingCalls < 200, host.WordFormingCalls + " word-forming questions after one keystroke in " + text.Length + " characters");
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
        });

        [Fact]
        public void An_edit_to_one_paragraph_leaves_the_other_paragraphs_checked_and_unchanged() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(20)), TestDocs.Para("en", "en", Sentence(20, "yyk")), TestDocs.Para("en", "en", Sentence(20, "xxj"))), host);
            CheckAll(f);
            var others = Enumerable.Range(0, 3).Select(i => f.SpellingMarks(i).ToList()).ToList();
            f.Editor.SetCaret(new TextPosition(1, 3));
            f.Editor.TypeText("zz");
            Assert.True(others[0].SequenceEqual(f.SpellingMarks(0)));
            Assert.True(others[2].SequenceEqual(f.SpellingMarks(2)));
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[1], host).SequenceEqual(f.SpellingMarks(1)));
        });

        [Fact]
        public void Enter_in_the_middle_of_a_paragraph_keeps_the_marks_of_both_halves_and_so_does_joining_them_again() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(24))), host);
            CheckAll(f);
            string text = f.Editor.Document.Paragraphs[0].Text;
            int split = text.IndexOf(' ', text.Length / 2);
            var all = f.SpellingMarks(0).ToList();
            f.Editor.SetCaret(new TextPosition(0, split));
            f.Editor.Enter();
            var left = f.SpellingMarks(0).ToList();
            var right = f.SpellingMarks(1).ToList();
            Assert.Equal(all.Count(m => m.End <= split), left.Count);
            Assert.True(left.Count > 0 && right.Count > 0);
            Assert.All(left, m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[0], host)));
            Assert.All(right, m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[1], host)));
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
            Assert.True(Reference(f.Editor.Document.Paragraphs[1], host).SequenceEqual(f.SpellingMarks(1)));
            f.Editor.SetCaret(new TextPosition(1, 0));
            f.Editor.Backspace();
            Assert.Single(f.Editor.Document.Paragraphs);
            Assert.All(f.SpellingMarks(0), m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[0], host)));
            Assert.True(f.SpellingMarks(0).Count >= all.Count - 2);
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
        });

        [Fact]
        public void Typing_a_space_or_an_apostrophe_that_splits_or_joins_words_leaves_the_right_marks()  => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            host.English.Known.Add("don't");
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", "the dont cat zzqx to")), host);
            CheckAll(f);
            Assert.Equal(new[] { "dont", "zzqx" }, f.SpellingMarks(0).Select(m => f.Editor.Document.Paragraphs[0].Text.Substring(m.Start, m.Length)).ToArray());
            f.Editor.SetCaret(new TextPosition(0, 7));        // between "don" and "t"
            f.Editor.TypeText("'");
            Assert.All(f.SpellingMarks(0), m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[0], host)));
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
            Assert.Single(f.SpellingMarks(0));                // "don't" is a word now
            f.Editor.SetCaret(new TextPosition(0, 8));
            f.Editor.TypeText(" ");                           // splits "don't" into "don'" and "t"? no: a space ends the word
            Assert.All(f.SpellingMarks(0), m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[0], host)));
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
            f.Editor.Backspace();
            Assert.All(f.SpellingMarks(0), m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[0], host)));
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
        });

        [Fact]
        public void Undo_and_redo_check_the_changed_paragraphs_again_and_end_with_the_right_marks() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(12)), TestDocs.Para("en", "en", Sentence(12, "ppk"))), host);
            var undo = new SimpleUndoStack();
            undo.Attach(f.Editor);
            CheckAll(f);
            f.Editor.SetCaret(new TextPosition(0, 4));
            f.Editor.TypeText("abc");
            CheckAll(f);
            undo.Undo();
            Assert.All(f.SpellingMarks(0), m => Assert.Contains(m, Reference(f.Editor.Document.Paragraphs[0], host)));
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
            undo.Redo();
            CheckAll(f);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
            Assert.True(Reference(f.Editor.Document.Paragraphs[1], host).SequenceEqual(f.SpellingMarks(1)));
        });

        // ---- random editing against a pass over the whole paragraph

        [Fact]
        public void After_any_sequence_of_edits_the_marks_are_those_of_a_full_pass_and_no_mark_shown_on_the_way_is_wrong() => Headless.Run(() =>
        {
            var rng = new Random(910);
            string[] pieces = { "a", "to", "zzq", "cat", " ", " ", " ", "'", "'s", "dog", "xx", "-", "1", "’" };
            int checkedStates = 0;
            string lastOp = string.Empty;
            for (int round = 0; round < 60; round++)
            {
                var host = new CountingSpellHost(Known);
                host.English.Known.Add("don't");
                var doc = TestDocs.Doc(
                    TestDocs.Para("en", "en", Sentence(rng.Next(3, 14))),
                    TestDocs.Para("en", "en", Sentence(rng.Next(0, 9), "wwp")),
                    TestDocs.Para("en", "en", string.Empty));
                var f = Field(doc, host);
                var undo = new SimpleUndoStack();
                undo.Attach(f.Editor);
                if (rng.Next(2) == 0) CheckAll(f);
                for (int step = 0; step < 40; step++)
                {
                    var e = f.Editor;
                    var para = rng.Next(e.Document.Paragraphs.Count);
                    e.SetCaret(new TextPosition(para, rng.Next(0, e.Document.Paragraphs[para].Length + 1)));
                    string before = string.Join("|", e.Document.Paragraphs.Select(x => x.Text)) + " caret " + e.Caret;
                    int op = rng.Next(9);
                    lastOp = "op " + op + " from [" + before + "]";
                    switch (op)
                    {
                        case 0: case 1: case 2: e.TypeText(pieces[rng.Next(pieces.Length)]); break;
                        case 3: e.Backspace(); break;
                        case 4: e.DeleteForward(); break;
                        case 5: e.Enter(); break;
                        case 6:
                        {
                            var other = new TextPosition(para, rng.Next(0, e.Document.Paragraphs[para].Length + 1));
                            e.SetCaret(other, true);
                            e.DeleteSelection();
                            break;
                        }
                        case 7: e.Paste(new List<Paragraph> { TestDocs.Para("en", "en", Sentence(rng.Next(1, 5), "kkv")) }); break;
                        default: if (rng.Next(2) == 0) undo.Undo(); else undo.Redo(); break;
                    }
                    for (int i = 0; i < e.Document.Paragraphs.Count; i++)
                    {
                        var reference = Reference(e.Document.Paragraphs[i], host);
                        Assert.All(f.SpellingMarks(i), m => Assert.True(reference.Contains(m), "round " + round + " step " + step + ": a mark " + m + " that a full pass does not make in [" + e.Document.Paragraphs[i].Text + "] after " + lastOp));
                    }
                    if (rng.Next(3) == 0) f.CheckSpellingStep();
                    if (rng.Next(7) == 0)
                    {
                        CheckAll(f);
                        for (int i = 0; i < e.Document.Paragraphs.Count; i++)
                        {
                            Assert.True(Reference(e.Document.Paragraphs[i], host).SequenceEqual(f.SpellingMarks(i)), "round " + round + " step " + step + " paragraph " + i + " [" + e.Document.Paragraphs[i].Text + "] after " + lastOp);
                            checkedStates++;
                        }
                    }
                }
                CheckAll(f);
                for (int i = 0; i < f.Editor.Document.Paragraphs.Count; i++)
                    Assert.True(Reference(f.Editor.Document.Paragraphs[i], host).SequenceEqual(f.SpellingMarks(i)), "round " + round + " end, paragraph " + i);
            }
            Assert.True(checkedStates > 100);
        });

        // ---- a line limit moves with an edit

        [Fact]
        public void An_edit_that_moves_a_line_limit_checks_the_words_it_brings_into_view_and_forgets_those_it_pushes_out() => Headless.Run(() =>
        {
            var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => "wrd" + i));
            var doc = TestDocs.Doc(TestDocs.Para("en", "en", words));
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var host = new FakeSpellHost();
            var f = Field(doc, host, 200);
            CheckAll(f);
            int visible = f.ParagraphLayoutFor(0).VisibleEnd;
            Assert.True(visible < words.Length);
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host, null, false).Count > 0);
            Assert.True(ReferenceSpellScanner.Check(f.Editor.Document.Paragraphs[0], host, null, false, visible).SequenceEqual(f.SpellingMarks(0)));

            // delete the first words: text from further on comes into view
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Editor.SetCaret(new TextPosition(0, 30), true);
            f.Editor.DeleteSelection();
            CheckAll(f);
            var p = f.Editor.Document.Paragraphs[0];
            int now = f.ParagraphLayoutFor(0).VisibleEnd;
            Assert.True(ReferenceSpellScanner.Check(p, host, null, false, now).SequenceEqual(f.SpellingMarks(0)), "after deleting: visible " + now + ", marks " + string.Join(",", f.SpellingMarks(0)));
            Assert.True(f.SpellingMarks(0).Count > 0 && f.SpellingMarks(0).Max(m => m.End) > visible - 30);

            // type at the start: text is pushed out of view and its marks go
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Editor.TypeText("a long start to push the rest out of view ");
            CheckAll(f);
            now = f.ParagraphLayoutFor(0).VisibleEnd;
            Assert.True(ReferenceSpellScanner.Check(f.Editor.Document.Paragraphs[0], host, null, false, now).SequenceEqual(f.SpellingMarks(0)));
        });

        // ---- one idle step is bounded by words

        [Fact]
        public void One_step_checks_a_bounded_number_of_words_of_a_long_paragraph_and_the_steps_together_check_it_all() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(4000))), host);
            var paragraph = f.Editor.Document.Paragraphs[0];
            int steps = 0, worstWordForming = 0, worstAsked = 0;
            int lastMarks = 0;
            bool grew = false;
            while (true)
            {
                host.ResetCounts();
                bool done = f.CheckSpellingStep();
                steps++;
                worstWordForming = Math.Max(worstWordForming, host.WordFormingCalls);
                worstAsked = Math.Max(worstAsked, host.English.Asked.Count);
                if (f.SpellingMarks(0).Count > lastMarks) grew = true;
                lastMarks = f.SpellingMarks(0).Count;
                if (done) break;
                Assert.False(f.IsSpellingComplete);
                Assert.True(steps < 1000);
            }
            Assert.True(steps > 20, steps + " steps for 4000 words");
            Assert.True(grew);
            Assert.True(worstAsked <= f.SpellStepWords, worstAsked + " words asked in one step");
            Assert.True(worstWordForming < 40 * f.SpellStepWords, worstWordForming + " word-forming questions in one step");
            Assert.True(Reference(paragraph, host).SequenceEqual(f.SpellingMarks(0)));
            Assert.True(f.IsSpellingComplete);
        });

        [Fact]
        public void A_step_of_one_word_still_goes_forward_and_every_word_is_asked_about_once() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(60))), host);
            f.SpellStepWords = 1;
            int steps = 0;
            while (!f.CheckSpellingStep() && steps < 500) steps++;
            Assert.True(steps >= 59 && steps < 500, steps + " steps");
            Assert.Equal(60, host.English.Asked.Count);      // each word once: a step does not look back at the word the last one ended with
            Assert.True(Reference(f.Editor.Document.Paragraphs[0], host).SequenceEqual(f.SpellingMarks(0)));
        });

        [Fact]
        public void A_step_that_is_given_the_whole_paragraph_to_do_asks_about_all_of_it_which_is_what_the_bound_prevents() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(4000))), host);
            f.SpellStepWords = int.MaxValue;     // the old behaviour: a paragraph a step
            host.ResetCounts();
            Assert.True(f.CheckSpellingStep());
            Assert.True(host.English.Asked.Count > 2000);
        });

        [Fact]
        public void The_idle_work_goes_on_until_a_paragraph_of_many_steps_is_done() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var host = new CountingSpellHost(Known);
            var (window, f) = Fixtures.InWindow(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(300))));
            f.SpellIdlePost = posts.Add;
            f.SpellChecking = host;
            int runs = 0;
            while (posts.Count > 0 && runs < 500)
            {
                var next = posts[0];
                posts.RemoveAt(0);
                next();
                runs++;
            }
            Assert.True(runs >= 300 / 25);
            Assert.True(f.IsSpellingComplete);
            Assert.Equal(75, f.SpellingMarks(0).Count);
            window.Close();
        });

        // ---- the answer for a menu or a hover

        [Fact]
        public void Asking_again_about_the_same_word_asks_the_host_nothing_and_a_change_makes_it_ask_once_more() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            host.English.Suggestions = new List<string> { "cat", "can" };
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", Sentence(2000))), host);
            string text = f.Editor.Document.Paragraphs[0].Text;
            int at = text.IndexOf("zzq1003", StringComparison.Ordinal) + 2;
            var first = f.SpellingSuggestionsAt(new TextPosition(0, at));
            Assert.NotNull(first);
            Assert.Equal(new[] { "cat", "can" }, first!.All);
            Assert.True(host.WordFormingCalls < 200, host.WordFormingCalls + " word-forming questions for one word of " + text.Length + " characters");
            host.ResetCounts();
            for (int i = 0; i < 50; i++)
            {
                var again = f.SpellingSuggestionsAt(new TextPosition(0, at + (i % 3) - 1));
                Assert.NotNull(again);
                Assert.Equal("zzq1003", again!.Word);
            }
            Assert.Equal(0, host.WordFormingCalls);
            Assert.Empty(host.English.Asked);
            // the answers are fresh objects, so a host that changes the parts it was given harms nobody
            var one = f.SpellingSuggestionsAt(new TextPosition(0, at))!;
            one.Parts[0].Text = "changed";
            Assert.Equal("zzq1003", f.SpellingSuggestionsAt(new TextPosition(0, at))!.Parts[0].Text);
            // an edit to the paragraph, or a restart, and the host is asked again
            f.Editor.SetCaret(new TextPosition(0, 1));
            f.Editor.TypeText("x");
            host.ResetCounts();
            Assert.NotNull(f.SpellingSuggestionsAt(new TextPosition(0, at + 1)));
            Assert.Single(host.English.Asked);
            host.ResetCounts();
            f.RestartSpelling();
            Assert.NotNull(f.SpellingSuggestionsAt(new TextPosition(0, at + 1)));
            Assert.Single(host.English.Asked);
        });

        [Fact]
        public void The_suggestions_for_a_word_are_asked_of_the_checker_once_until_checking_restarts() => Headless.Run(() =>
        {
            var host = new CountingSpellHost(Known);
            host.English.Suggestions = new List<string> { "cat" };
            int suggested = 0;
            var counting = new CountingSuggest(host.English, () => suggested++);
            host.English.Known.Add("zzq9");
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", "the zzq3 and zzq7 and zzq3")), new CustomHost(counting));
            for (int i = 0; i < 5; i++) f.SpellingSuggestionsAt(new TextPosition(0, 6));
            f.SpellingSuggestionsAt(new TextPosition(0, 22));      // the same word again, elsewhere in the paragraph
            Assert.Equal(1, suggested);
            f.SpellingSuggestionsAt(new TextPosition(0, 15));      // another word
            Assert.Equal(2, suggested);
            f.RestartSpelling();
            f.SpellingSuggestionsAt(new TextPosition(0, 6));
            Assert.Equal(3, suggested);
        });

        sealed class CustomHost : SpellCheckHostBase
        {
            readonly ISpellChecker _checker;
            public CustomHost(ISpellChecker checker) { _checker = checker; }
            public override ISpellChecker? GetChecker(string ws) => _checker;
        }

        sealed class CountingSuggest : ISpellChecker
        {
            readonly ISpellChecker _inner;
            readonly Action _counted;
            public CountingSuggest(ISpellChecker inner, Action counted) { _inner = inner; _counted = counted; }
            public bool Check(string word) => _inner.Check(word);
            public IReadOnlyList<string> Suggest(string word) { _counted(); return _inner.Suggest(word); }
            public void Add(string word) => _inner.Add(word);
        }
    }
}
