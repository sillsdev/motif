using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 103: a checker that says it is safe off the UI thread (<see cref="IBackgroundSpellChecker"/>) is asked its words on a worker, the
    /// field finds the words and applies the answers on its own thread, and an answer for text that has changed since is dropped. A scheduler seam
    /// (<c>SpellBackgroundRun</c>, with <c>SpellIdlePost</c> for the way back) makes the order deterministic.</summary>
    public class SpellingBackgroundTests
    {
        sealed class Host : SpellCheckHostBase
        {
            public readonly Dictionary<string, ISpellChecker> Checkers = new Dictionary<string, ISpellChecker>();
            public override ISpellChecker? GetChecker(string ws) => Checkers.TryGetValue(ws, out var c) ? c : null;
        }

        /// <summary>A checker that is safe off the UI thread: it knows some words and records the thread of each question.</summary>
        sealed class Bg : IBackgroundSpellChecker
        {
            public readonly HashSet<string> Known = new HashSet<string>();
            public readonly List<string> Asked = new List<string>();
            public readonly List<int> Threads = new List<int>();
            public Exception? Throws;
            /// <summary>When set, a question from a thread other than <see cref="GateOwner"/> sets Entered and waits for Release before answering.</summary>
            public ManualResetEventSlim? Entered, Release;
            public int GateOwner;
            int _inside;
            public int MaxInside;
            /// <summary>Called after each question with how many have been asked.</summary>
            public Action<int>? AfterCheck;

            public bool Check(string word)
            {
                int now = Interlocked.Increment(ref _inside);
                try
                {
                    lock (Asked)
                    {
                        Asked.Add(word);
                        Threads.Add(Environment.CurrentManagedThreadId);
                        MaxInside = Math.Max(MaxInside, now);
                    }
                    if (Release != null && Environment.CurrentManagedThreadId != GateOwner)
                    {
                        Entered!.Set();
                        Release.Wait();
                    }
                    int count;
                    lock (Asked) count = Asked.Count;
                    AfterCheck?.Invoke(count);
                    if (Throws != null) throw Throws;
                    return Known.Contains(word);
                }
                finally { Interlocked.Decrement(ref _inside); }
            }

            public IReadOnlyList<string> Suggest(string word) => new[] { "the" };
            public void Add(string word) => Known.Add(word);
        }

        /// <summary>A checker that does not say so: asked inline, as before.</summary>
        sealed class Plain : ISpellChecker
        {
            public readonly HashSet<string> Known = new HashSet<string>();
            public readonly List<string> Asked = new List<string>();
            public bool Check(string word) { Asked.Add(word); return Known.Contains(word); }
            public IReadOnlyList<string> Suggest(string word) => new string[0];
            public void Add(string word) => Known.Add(word);
        }

        /// <summary>A field in a window whose idle posts and worker runs are collected, so a test runs them in the order it chooses.</summary>
        sealed class Rig : IDisposable
        {
            public readonly RichTextField Field;
            public readonly Window Window;
            public readonly List<Action> Idle = new List<Action>();
            public readonly List<Action> Worker = new List<Action>();

            public Rig(Document doc, ISpellCheckHost host)
            {
                (Window, Field) = Fixtures.InWindow(doc, 520);
                Field.SpellIdlePost = Idle.Add;
                Field.SpellBackgroundRun = Worker.Add;
                Field.SpellChecking = host;            // asks for idle time: one step is waiting in Idle
            }

            public void Dispose() => Window.Close();

            public Paragraph P(int i = 0) => Field.Editor.Document.Paragraphs[i];

            public string[] Words(int p = 0) => Field.SpellingMarks(p).Select(m => P(p).Text.Substring(m.Start, m.Length)).ToArray();

            /// <summary>The idle step that is waiting, run now: it finds the words and sends the dictionary questions to the worker.</summary>
            public void Step()
            {
                Assert.Single(Idle);
                var step = Idle[0];
                Idle.RemoveAt(0);
                step();
            }

            /// <summary>The worker asks the batch that was sent to it; its answer is posted back, not applied.</summary>
            public void WorkerRuns()
            {
                Assert.Single(Worker);
                var run = Worker[0];
                Worker.RemoveAt(0);
                run();
            }

            /// <summary>Runs whatever is waiting, the worker and the field's thread alike, first in first out, until nothing is.</summary>
            public void RunAll()
            {
                // Bounded, so a stale answer that starts the work over for ever (a bug, not a slow test) fails here instead of hanging the run.
                for (int steps = 0; Idle.Count > 0 || Worker.Count > 0; steps++)
                {
                    if (steps > 10000) throw new Xunit.Sdk.XunitException("The idle work and the worker did not come to an end after " + steps + " runs.");
                    var next = Worker.Count > 0 ? Worker : Idle;
                    var a = next[0];
                    next.RemoveAt(0);
                    a();
                }
            }
        }

        static Document Two() => Doc(Para("en", "en", "the cat saat down"), Para("en", "en", "and teh end"));

        static Host BgHost(out Bg english)
        {
            english = new Bg();
            foreach (var w in new[] { "the", "cat", "down", "and", "end", "The" }) english.Known.Add(w);
            var host = new Host();
            host.Checkers["en"] = english;
            return host;
        }

        // ---- the order of things

        [Fact]
        public void A_step_finds_the_words_on_the_field_and_hands_the_dictionary_questions_to_the_worker() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            Assert.Empty(en.Asked);                          // nothing asked yet: the questions went to the worker
            Assert.Single(rig.Worker);
            Assert.False(rig.Field.IsSpellingComplete);
            rig.WorkerRuns();
            Assert.Equal(new[] { "the", "cat", "saat", "down" }, en.Asked);
            Assert.Empty(rig.Field.SpellingMarks(0));        // the answer is not applied until the field's own thread does it
            Assert.False(rig.Field.IsSpellingComplete);
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.Equal(new[] { "saat" }, rig.Words(0));
            Assert.Equal(new[] { "teh" }, rig.Words(1));
        });

        [Fact]
        public void One_batch_is_out_at_a_time_and_a_step_while_it_is_out_starts_nothing_more() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            Assert.Single(rig.Worker);
            for (int i = 0; i < 5; i++) Assert.False(rig.Field.CheckSpellingStep());
            Assert.Single(rig.Worker);
            Assert.Empty(rig.Idle);                          // and it does not busy-wait: the answer asks for the next step
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
        });

        [Fact]
        public void A_step_is_still_bounded_by_words_and_the_rest_is_asked_in_later_batches() => Headless.Run(() =>
        {
            var doc = Doc(Para("en", "en", string.Join(" ", Enumerable.Range(0, 60).Select(i => "w" + i))));
            using var rig = new Rig(doc, BgHost(out var en));
            rig.Step();
            rig.WorkerRuns();
            Assert.Equal(25, en.Asked.Count);                // one step's worth
            rig.RunAll();
            Assert.Equal(60, en.Asked.Count);
            Assert.Equal(60, rig.Field.SpellingMarks(0).Count);
            Assert.True(rig.Field.IsSpellingComplete);
        });

        [Fact]
        public void With_the_real_scheduler_the_questions_run_off_the_field_thread_and_the_answers_are_applied_on_it() => Headless.Run(() =>
        {
            var host = BgHost(out var en);
            var (window, field) = Fixtures.InWindow(Two());
            var signal = new ManualResetEventSlim();         // set whenever the field posts to its dispatcher: an idle request or the worker's answer
            field.SpellIdlePost = a => { global::Avalonia.Threading.Dispatcher.UIThread.Post(a, global::Avalonia.Threading.DispatcherPriority.Background); signal.Set(); };
            field.SpellChecking = host;
            Headless.Pump();
            while (!field.IsSpellingComplete)
            {
                Assert.True(signal.Wait(60000), "the field never asked for more idle time");   // an event, not a poll; the limit only stops a broken field hanging the run
                signal.Reset();
                Headless.Pump();
            }
            Assert.True(field.IsSpellingComplete);
            Assert.NotEmpty(en.Threads);
            Assert.DoesNotContain(Environment.CurrentManagedThreadId, en.Threads);
            Assert.Equal(new[] { "saat" }, field.SpellingMarks(0).Select(m => field.Editor.Document.Paragraphs[0].Text.Substring(m.Start, m.Length)));
            window.Close();
        });

        // ---- an answer for text that has changed is dropped

        [Fact]
        public void An_edit_between_the_question_and_the_answer_drops_the_answer_and_the_paragraph_is_asked_again() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            rig.WorkerRuns();                               // the worker has asked; its answer is on the way
            rig.Field.Editor.SetCaret(new TextPosition(0, 0));
            rig.Field.Editor.TypeText("zz");                // "zzthe cat saat down": every offset moved
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.Equal(new[] { "zzthe", "saat" }, rig.Words(0));
            Assert.Equal(new[] { "teh" }, rig.Words(1));
            Assert.Equal(ReferenceSpellScanner.Check(rig.P(0), rig.Field.SpellChecking!), rig.Field.SpellingMarks(0).ToList());
        });

        [Fact]
        public void A_change_the_host_makes_directly_between_the_question_and_the_answer_is_noticed_by_the_paragraph_stamp() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            rig.WorkerRuns();
            rig.P(0).Runs[0].Text = "saat saat the cat";     // no editor, no call: only the paragraph's stamp moves
            Fixtures.Layout(rig.Field, 520);                 // and the field lays the paragraph out again, with no marks yet
            Assert.Empty(rig.Field.ParagraphLayoutFor(0).SpellingMarksDrawn);
            Assert.NotEmpty(rig.Idle);                       // the answer for the old text is waiting
            var answer = rig.Idle[0];
            rig.Idle.RemoveAt(0);
            answer();                                        // it must not put marks made for the old text on the new layout
            Assert.Empty(rig.Field.ParagraphLayoutFor(0).SpellingMarksDrawn);
            Assert.Empty(rig.Field.SpellingMarks(0));
            rig.RunAll();
            Assert.Equal(new[] { "saat", "saat" }, rig.Words(0));
            Assert.Equal(2, rig.Field.ParagraphLayoutFor(0).SpellingMarksDrawn.Count);
        });

        [Fact]
        public void Starting_checking_over_between_the_question_and_the_answer_drops_the_answer_and_the_next_batch_waits_for_it() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            rig.WorkerRuns();
            en.Known.Add("saat");                            // the dictionary changed meanwhile, and the field was told
            rig.Field.RestartSpelling();
            Assert.Empty(rig.Field.SpellingMarks(0));
            Assert.False(rig.Field.CheckSpellingStep());     // the old batch has not come back: nothing new is sent while it is out
            Assert.Empty(rig.Worker);
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.Empty(rig.Words(0));                      // "saat" is right now: the old answer, that it is not, was not used
            Assert.Equal(new[] { "teh" }, rig.Words(1));
        });

        [Fact]
        public void A_width_change_that_moves_a_line_limit_between_the_question_and_the_answer_drops_the_answer() => Headless.Run(() =>
        {
            var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => "wrd" + i));
            var doc = Doc(Para("en", "en", words));
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            using var rig = new Rig(doc, BgHost(out var en));
            Fixtures.Layout(rig.Field, 200);
            rig.Step();
            rig.WorkerRuns();                                // asked for the words that showed at 200 pixels
            Fixtures.Layout(rig.Field, 2000);                // the limit moves: what was checked of such a paragraph is forgotten
            var answer = rig.Idle[0];
            rig.Idle.RemoveAt(0);
            answer();
            Assert.Empty(rig.Field.ParagraphLayoutFor(0).SpellingMarksDrawn);   // the answer was for another cut of the text
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            int visible = rig.Field.ParagraphLayoutFor(0).VisibleEnd;
            Assert.True(visible > 200 && visible < words.Length, "the limit still cuts the text at " + visible);
            Assert.Equal(SpellScanner.Check(rig.P(0), rig.Field.SpellChecking!, limit: visible), rig.Field.SpellingMarks(0).ToList());   // what shows now
        });

        [Fact]
        public void A_failure_that_comes_back_after_checking_started_over_is_not_reported_against_the_new_checking() => Headless.Run(() =>
        {
            var host = BgHost(out var en);
            en.Throws = new InvalidOperationException("dictionary gone");
            using var rig = new Rig(Two(), host);
            int seen = 0;
            rig.Field.SpellCheckerFailed += (ws, e) => seen++;
            rig.Step();
            rig.WorkerRuns();
            en.Throws = null;                                // the host repaired it and the field was told
            rig.Field.RestartSpelling();
            rig.RunAll();
            Assert.Equal(0, seen);
            Assert.Equal(new[] { "saat" }, rig.Words(0));    // and the writing system is checked again
        });

        [Fact]
        public void A_menu_asked_while_a_batch_is_out_calls_Check_on_the_field_thread_at_the_same_time_as_the_worker() => Headless.Run(() =>
        {
            var host = BgHost(out var en);
            en.Entered = new ManualResetEventSlim();
            en.Release = new ManualResetEventSlim();
            en.GateOwner = Environment.CurrentManagedThreadId;
            using var rig = new Rig(Two(), host);
            rig.Step();
            var run = rig.Worker[0];
            var worker = new Thread(() => run());
            rig.Worker.Clear();
            worker.Start();
            en.Entered.Wait();                               // the worker is inside Check
            var s = rig.Field.SpellingSuggestionsAt(new TextPosition(0, 10));   // the menu: Check on this thread now
            en.Release.Set();
            worker.Join();
            Assert.NotNull(s);
            Assert.Equal(2, en.MaxInside);                   // so a background checker's Check must be fully thread-safe
            rig.RunAll();
        });

        // ---- a batch that is out is cancelled when the field lets go of the host

        [Fact]
        public void Taking_the_host_away_while_a_batch_is_out_stops_the_worker_asking_the_old_checker() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            en.AfterCheck = n => { if (n == 2) rig.Field.SpellChecking = null; };
            rig.WorkerRuns();
            Assert.Equal(2, en.Asked.Count);                 // the third and fourth words of the step were not asked
            rig.RunAll();
            Assert.Empty(rig.Field.SpellingMarks(0));
        });

        [Fact]
        public void Leaving_the_visual_tree_while_a_batch_is_out_stops_the_worker_asking_and_coming_back_asks_again() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            en.AfterCheck = n => { if (n == 2) rig.Window.Content = null; };
            rig.WorkerRuns();
            Assert.Equal(2, en.Asked.Count);
            en.AfterCheck = null;
            rig.RunAll();
            Assert.Empty(rig.Field.SpellingMarks(0));        // the half answer was dropped
            rig.Window.Content = rig.Field;                  // back on screen: everything in view is asked again
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.Equal(new[] { "saat" }, rig.Words(0));
        });

        [Fact]
        public void Starting_checking_over_while_a_batch_is_out_stops_the_worker_asking_the_rest_of_it() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            en.AfterCheck = n => { if (n == 2) rig.Field.RestartSpelling(); };
            rig.WorkerRuns();
            Assert.Equal(2, en.Asked.Count);
            en.AfterCheck = null;
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.Equal(new[] { "saat" }, rig.Words(0));
        });

        [Fact]
        public void Starting_checking_over_works_even_when_the_checker_never_returned_and_its_late_answer_is_dropped() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            var hung = Assert.Single(rig.Worker);            // the batch is out and, for now, never comes back
            rig.Worker.Clear();
            rig.Idle.Clear();
            rig.Field.RestartSpelling();                     // the same host: its checker may still be stuck on the old question
            Assert.NotEmpty(rig.Idle);
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.Equal(new[] { "saat" }, rig.Words(0));
            hung();                                          // the old question finally returns
            rig.RunAll();
            Assert.Equal(new[] { "saat" }, rig.Words(0));    // dropped: no double marks
            Assert.True(rig.Field.IsSpellingComplete);
        });

        [Fact]
        public void Leaving_the_visual_tree_after_the_answers_arrived_keeps_them() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.Step();
            rig.WorkerRuns();                                // every word is answered; the answer waits to be applied on the field's thread
            int asked = en.Asked.Count;
            rig.Window.Content = null;
            rig.RunAll();
            Assert.Equal(new[] { "saat" }, rig.Words(0));    // applied, not thrown away and asked again
            Assert.Equal(asked, en.Asked.Count);
        });

        [Fact]
        public void A_new_host_works_even_when_the_old_checker_never_returned() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var hung));
            rig.Step();
            Assert.Single(rig.Worker);                       // the batch is out and, for this test, never comes back
            rig.Worker.Clear();
            rig.Idle.Clear();
            rig.Field.SpellChecking = BgHost(out var fresh);
            Assert.NotEmpty(rig.Idle);
            rig.RunAll();
            Assert.True(rig.Field.IsSpellingComplete);
            Assert.NotEmpty(fresh.Asked);
            Assert.Equal(new[] { "saat" }, rig.Words(0));
        });

        // ---- failures stay on the field's thread

        [Fact]
        public void A_checker_that_throws_on_the_worker_is_reported_on_the_field_thread_once_and_left_alone() => Headless.Run(() =>
        {
            var host = BgHost(out var en);
            en.Throws = new InvalidOperationException("dictionary gone");
            using var rig = new Rig(Two(), host);
            var seen = new List<(string Ws, int Thread)>();
            rig.Field.SpellCheckerFailed += (ws, e) => seen.Add((ws, Environment.CurrentManagedThreadId));
            rig.Step();
            rig.WorkerRuns();                                // the exception happens here, on the "worker"
            Assert.Empty(seen);                              // the field has not heard of it yet
            rig.RunAll();
            Assert.Equal(new[] { ("en", Environment.CurrentManagedThreadId) }, seen);
            Assert.Empty(rig.Words(0));                      // a word that cannot be judged is taken as right
            Assert.Single(en.Asked);                         // and after the first failure nothing more was asked of it
            Assert.True(rig.Field.IsSpellingComplete);
        });

        // ---- the synchronous path is what it was

        [Fact]
        public void A_checker_that_does_not_say_it_is_safe_off_the_thread_is_asked_inline_as_before() => Headless.Run(() =>
        {
            var plain = new Plain();
            foreach (var w in new[] { "the", "cat", "down", "and", "end" }) plain.Known.Add(w);
            var host = new Host();
            host.Checkers["en"] = plain;
            using var rig = new Rig(Two(), host);
            while (!rig.Field.CheckSpellingStep()) { }
            Assert.Empty(rig.Worker);
            Assert.Equal(new[] { "saat" }, rig.Words(0));
            Assert.True(rig.Field.IsSpellingComplete);
        });

        [Fact]
        public void A_paragraph_with_one_writing_system_that_is_safe_and_one_that_is_not_gets_the_marks_of_both() => Headless.Run(() =>
        {
            var host = BgHost(out var en);
            var fr = new Plain();
            fr.Known.Add("bon");
            host.Checkers["fr"] = fr;
            using var rig = new Rig(Doc(Para("en", "en", "the saat ", "fr", "bon mauvais")), host);
            rig.Step();
            Assert.Equal(new[] { "bon", "mauvais" }, fr.Asked);   // the one that is not safe was asked at once, on the field's thread
            Assert.Empty(en.Asked);
            rig.RunAll();
            Assert.Equal(new[] { "saat", "mauvais" }, rig.Words(0));
            Assert.True(rig.Field.IsSpellingComplete);
        });

        [Fact]
        public void The_questions_for_a_menu_are_still_asked_on_the_field_thread() => Headless.Run(() =>
        {
            using var rig = new Rig(Two(), BgHost(out var en));
            rig.RunAll();
            var s = rig.Field.SpellingSuggestionsAt(new TextPosition(0, 10));
            Assert.NotNull(s);
            Assert.Equal("saat", s!.Word);
            Assert.Equal(new[] { "the" }, s.All);
        });
    }
}
