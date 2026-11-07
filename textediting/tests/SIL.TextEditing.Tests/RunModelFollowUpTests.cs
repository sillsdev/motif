using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Issue 107: what the run model promised a host that the first version of the run index (issue 76) left loose: reads from other
    /// threads, enumerating while the list changes, assigning a <see cref="List{T}"/>, and work counters that stay out of a release build.</summary>
    public class RunModelFollowUpTests
    {
        static Paragraph Three() => Para("en", "en", "aa", "fr", "bb", "en", "cc");

        // ---- Paragraph.Text read from several threads

        [Fact]
        public void Reading_a_paragraphs_text_from_several_threads_after_a_change_never_gives_the_text_from_before_it()
        {
            // Nothing writes while the readers run, so every read must give the text the paragraph holds. The first read after a change fills the
            // cache; a stamp and a string written one after the other let a second reader take the new stamp with the old string.
            const int rounds = 3000;
            var p = Three();
            using var barrier = new Barrier(4);
            int stale = 0;
            var readers = Enumerable.Range(0, 3).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < rounds; i++)
                {
                    barrier.SignalAndWait();
                    if (p.Text != "aabbcc" + new string('x', i % 5 + 1)) Interlocked.Increment(ref stale);
                    barrier.SignalAndWait();
                }
            })).ToArray();
            foreach (var t in readers) t.Start();
            for (int i = 0; i < rounds; i++)
            {
                p.Runs[2].Text = "cc" + new string('x', i % 5 + 1);            // changes the text; the cache for the old text is now stale
                barrier.SignalAndWait();
                barrier.SignalAndWait();
            }
            foreach (var t in readers) t.Join();
            Assert.Equal(0, stale);
        }

        [Fact]
        public void The_text_of_a_paragraph_follows_each_change_to_its_runs()
        {
            var p = Three();
            Assert.Equal("aabbcc", p.Text);
            p.Runs[1].Text = "BB";
            Assert.Equal("aaBBcc", p.Text);
            p.Runs.RemoveAt(0);
            Assert.Equal("BBcc", p.Text);
            p.Runs.Add(new Run { Text = "!" });
            Assert.Equal("BBcc!", p.Text);
        }

        // ---- enumerating while the list changes

        [Fact]
        public void Adding_to_the_runs_while_looping_over_them_throws_as_a_list_does()
        {
            var p = Three();
            Assert.Throws<InvalidOperationException>(() =>
            {
                int steps = 0;
                foreach (var r in p.Runs)
                {
                    p.Runs.Add(new Run { Text = "x" });
                    if (++steps > 100) break;                         // a loop that never notices would otherwise not end
                }
            });
        }

        [Fact]
        public void Every_change_to_the_list_during_a_loop_throws_on_the_next_step()
        {
            var changes = new Dictionary<string, Action<RunList>>
            {
                ["Add"] = l => l.Add(new Run { Text = "x" }),
                ["Insert"] = l => l.Insert(0, new Run { Text = "x" }),
                ["RemoveAt"] = l => l.RemoveAt(0),
                ["Remove"] = l => l.Remove(l[0]),
                ["RemoveRange"] = l => l.RemoveRange(0, 1),
                ["Clear"] = l => l.Clear(),
                ["AddRange"] = l => l.AddRange(new[] { new Run { Text = "x" } }),
                ["Set"] = l => l[0] = new Run { Text = "y" },
            };
            foreach (var change in changes)
            {
                var p = Three();
                var e = p.Runs.GetEnumerator();
                Assert.True(e.MoveNext());
                change.Value(p.Runs);
                Assert.True(Throws(ref e), change.Key + " did not make the loop throw");
                // The same through the interface, as LINQ and a foreach over IEnumerable<Run> see it.
                var q = Three();
                using var ie = ((IEnumerable<Run>)q.Runs).GetEnumerator();
                Assert.True(ie.MoveNext());
                change.Value(q.Runs);
                Assert.Throws<InvalidOperationException>(() => ie.MoveNext());
            }
        }

        static bool Throws(ref RunList.Enumerator e)
        {
            try { e.MoveNext(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        [Fact]
        public void Changing_a_runs_text_or_properties_while_looping_over_the_runs_is_allowed()
        {
            // A List<Run> does not see a change to an item either: only the list changing ends a loop.
            var p = Three();
            foreach (var r in p.Runs)
            {
                r.Text += "!";
                r.Ws = "fr";
                r.Props = new PropertyBag().SetInt(TextPropertyNumbers.Bold, 1);
            }
            Assert.Equal("aa!bb!cc!", p.Text);
        }

        [Fact]
        public void A_loop_that_normalizes_or_edits_through_the_paragraph_after_it_ends_is_unaffected()
        {
            var p = Three();
            foreach (var r in p.Runs) _ = r.Text;
            p.Runs.Add(new Run { Text = "d" });
            int n = 0;
            foreach (var r in p.Runs) n++;
            Assert.Equal(4, n);
        }

        // ---- a List<Run> assigned to Paragraph.Runs

        [Fact]
        public void A_list_of_runs_can_be_assigned_to_a_paragraph_and_its_runs_are_the_paragraphs()
        {
            var list = new List<Run> { new Run { Ws = "en", Text = "ab" }, new Run { Ws = "fr", Text = "cde" } };
            var p = new Paragraph();
            p.Runs = list;
            Assert.Equal(5, p.Length);
            Assert.Equal("abcde", p.Text);
            Assert.Same(list[1], p.Runs[1]);
            long stamp = p.ChangeStamp;
            list[1].Text = "c";
            Assert.NotEqual(stamp, p.ChangeStamp);
            Assert.Equal(3, p.Length);
            list.Add(new Run { Text = "later" });          // the paragraph holds a copy of the list: this one is no longer its list
            Assert.Equal(2, p.Runs.Count);
        }

        [Fact]
        public void Assigning_a_null_list_of_runs_throws()
        {
            var p = new Paragraph();
            List<Run>? none = null;
            Assert.Throws<ArgumentNullException>(() => p.Runs = none!);
        }

        // ---- the counters stay out of a release build

#if TEXTEDITING_COUNTERS
        [Fact]
        public void A_build_with_the_counters_counts_run_reads_and_text_reads()
        {
            var p = Three();
            _ = p.Text;                                    // joining the text the first time reads every run; the second time reads none
            long reads = p.Runs.ElementReads, texts = p.TextReads;
            _ = p.Runs[0];
            foreach (var r in p.Runs) _ = r;
            _ = p.Text;
            Assert.Equal(reads + 1 + 3, p.Runs.ElementReads);
            Assert.Equal(texts + 1, p.TextReads);
        }
#else
        [Fact]
        public void A_build_without_the_counters_counts_nothing_on_the_hot_paths()
        {
            var p = Three();
            _ = p.Runs[0];
            foreach (var r in p.Runs) _ = r;
            _ = p.Text;
            Assert.Equal(0, p.Runs.ElementReads);
            Assert.Equal(0, p.TextReads);
        }
#endif
    }
}
