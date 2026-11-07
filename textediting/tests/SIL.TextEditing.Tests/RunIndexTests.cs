using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Issue 76: the run lookups and the length are answered from run starts the paragraph keeps up to date, however the
    /// runs change, and a lookup does not read every run.</summary>
    public class RunIndexTests
    {
        // What the paragraph answered before the index: a scan that adds up the lengths of the runs in front.
        static List<Run> Snapshot(Paragraph p) => p.Runs.ToList();

        static int OldLength(Paragraph p) { int n = 0; foreach (var r in Snapshot(p)) n += r.Text.Length; return n; }

        static Run? OldBefore(Paragraph p, int offset, out int start)
        {
            int pos = 0; start = 0;
            foreach (var r in Snapshot(p))
            {
                if (r.Text.Length > 0 && offset > pos && offset <= pos + r.Text.Length) { start = pos; return r; }
                pos += r.Text.Length;
            }
            return null;
        }

        static Run? OldAfter(Paragraph p, int offset, out int start)
        {
            int pos = 0; start = 0;
            foreach (var r in Snapshot(p))
            {
                if (offset >= pos && offset < pos + r.Text.Length) { start = pos; return r; }
                pos += r.Text.Length;
            }
            return null;
        }

        static void AssertSameAsScan(Paragraph p)
        {
            int length = OldLength(p);
            Assert.Equal(length, p.Length);
            Assert.Equal(string.Concat(Snapshot(p).Select(r => r.Text)), p.Text);
            for (int offset = -2; offset <= length + 2; offset++)
            {
                var wantB = OldBefore(p, offset, out int sb);
                var gotB = p.RunBefore(offset, out int gb);
                Assert.Same(wantB, gotB);
                if (wantB != null) Assert.Equal(sb, gb);
                var wantA = OldAfter(p, offset, out int sa);
                var gotA = p.RunAfter(offset, out int ga);
                Assert.Same(wantA, gotA);
                if (wantA != null) Assert.Equal(sa, ga);
            }
            var spans = p.Spans();
            var runs = Snapshot(p);
            int pos = 0;
            Assert.Equal(runs.Count, spans.Count);
            for (int i = 0; i < spans.Count; i++)
            {
                Assert.Equal(pos, spans[i].Start);
                Assert.Same(runs[i], spans[i].Run);
                pos += spans[i].Length;
            }
        }

        static Paragraph Random(Random rng, int runs)
        {
            var p = new Paragraph { DefaultWs = "en" };
            for (int i = 0; i < runs; i++)
            {
                int kind = rng.Next(5);
                p.Runs.Add(kind == 0 ? new Run { Ws = "en", Text = string.Empty }
                    : kind == 1 ? Run.Object("en", "o" + i)
                    : new Run { Ws = i % 2 == 0 ? "en" : "fr", Text = new string('a', rng.Next(1, 6)) });
            }
            return p;
        }

        [Fact]
        public void The_lookups_match_a_plain_scan_for_random_paragraphs_with_empty_and_object_runs()
        {
            var rng = new Random(76);
            for (int round = 0; round < 60; round++)
                AssertSameAsScan(Random(rng, rng.Next(0, 14)));
        }

        [Fact]
        public void The_lookups_follow_every_kind_of_direct_change_to_the_runs()
        {
            var rng = new Random(77);
            for (int round = 0; round < 40; round++)
            {
                var p = Random(rng, rng.Next(1, 10));
                AssertSameAsScan(p);
                p.Runs[rng.Next(p.Runs.Count)].Text = "changed text";           // a direct edit of a run's text
                AssertSameAsScan(p);
                p.Runs.Insert(rng.Next(p.Runs.Count + 1), new Run { Ws = "en", Text = "ins" });
                AssertSameAsScan(p);
                p.Runs[rng.Next(p.Runs.Count)] = new Run { Ws = "de", Text = "swapped in" };
                AssertSameAsScan(p);
                p.Runs.RemoveAt(rng.Next(p.Runs.Count));
                AssertSameAsScan(p);
                p.Runs.Add(new Run { Ws = "en", Text = "tail" });
                AssertSameAsScan(p);
                if (p.Runs.Count > 2) p.Runs.RemoveRange(1, 2);
                AssertSameAsScan(p);
                p.Runs.AddRange(new[] { new Run { Ws = "en", Text = "x" }, new Run { Ws = "en", Text = "yy" } });
                AssertSameAsScan(p);
                p.Runs = new RunList(new[] { new Run { Ws = "en", Text = "replaced" } });
                AssertSameAsScan(p);
                p.Normalize();
                AssertSameAsScan(p);
                p.Runs.Clear();
                AssertSameAsScan(p);
            }
        }

        [Fact]
        public void The_length_is_right_after_a_run_the_paragraph_had_already_measured_is_edited_directly()
        {
            var p = Para("en", "en", "abc", "fr", "defg");
            Assert.Equal(7, p.Length);
            Assert.Equal("fr", p.RunAfter(4, out int start)!.Ws);
            Assert.Equal(3, start);
            p.Runs[0].Text = "abcdefgh";                                   // grows the first run behind the paragraph's back
            Assert.Equal(12, p.Length);
            Assert.Equal("fr", p.RunAfter(9, out start)!.Ws);
            Assert.Equal(8, start);
            Assert.Equal("en", p.RunBefore(8, out start)!.Ws);
        }

        [Fact]
        public void A_run_held_by_two_paragraphs_keeps_both_right_and_a_removed_run_no_longer_matters()
        {
            var shared = new Run { Ws = "en", Text = "shared" };
            var one = new Paragraph(); one.Runs.Add(shared); one.Runs.Add(new Run { Ws = "en", Text = "-1" });
            var two = new Paragraph(); two.Runs.Add(new Run { Ws = "en", Text = "2-" }); two.Runs.Add(shared);
            Assert.Equal(8, one.Length); Assert.Equal(8, two.Length);
            shared.Text = "x";
            AssertSameAsScan(one); AssertSameAsScan(two);
            Assert.Equal(3, one.Length); Assert.Equal(3, two.Length);
            one.Runs.Remove(shared);
            shared.Text = "longer";
            Assert.Equal(2, one.Length);
            Assert.Equal(8, two.Length);
            AssertSameAsScan(one); AssertSameAsScan(two);
        }

        [Fact]
        public void The_same_run_twice_in_one_list_counts_twice()
        {
            var r = new Run { Ws = "en", Text = "ab" };
            var p = new Paragraph(); p.Runs.Add(r); p.Runs.Add(r);
            Assert.Equal(4, p.Length);
            r.Text = "abc";
            Assert.Equal(6, p.Length);
            p.Runs.RemoveAt(0);
            r.Text = "a";
            Assert.Equal(1, p.Length);
            AssertSameAsScan(p);
        }

        [Fact]
        public void A_run_list_replaced_by_assignment_stops_telling_the_paragraph()
        {
            var p = Para("en", "en", "abc");
            var old = p.Runs;
            var first = old[0];
            p.Runs = new RunList(new[] { new Run { Ws = "en", Text = "zz" } });
            first.Text = "a much longer text";
            Assert.Equal(2, p.Length);
            old.Add(new Run { Text = "ignored" });
            Assert.Equal(2, p.Length);
        }

        [Fact]
        public void Assigning_a_list_another_paragraph_holds_copies_it_instead_of_sharing_it()
        {
            var a = Para("en", "en", "abc");
            var b = new Paragraph();
            b.Runs = a.Runs;
            Assert.NotSame(a.Runs, b.Runs);
            a.Runs[0].Text = "abcdef";                                     // the runs themselves are shared, so both are told
            Assert.Equal(6, a.Length);
            Assert.Equal(6, b.Length);
            b.Runs.Add(new Run { Ws = "en", Text = "!" });
            Assert.Equal(6, a.Length);
            Assert.Equal(7, b.Length);
        }

        static Paragraph Many(int count)
        {
            var p = new Paragraph { DefaultWs = "en" };
            for (int i = 0; i < count; i++)
            {
                var r = new Run { Ws = "en", Text = "ab" };
                r.Props.SetInt(TextPropertyNumbers.Editable, i % 2 == 0 ? TextPropertyNumbers.EditableNo : TextPropertyNumbers.EditableYes);
                p.Runs.Add(r);
            }
            return p;
        }

#if TEXTEDITING_COUNTERS
        [Fact]
        public void Looking_up_runs_and_the_length_reads_a_few_runs_not_all_of_them()
        {
            const int runs = 4000;
            var p = Many(runs);
            _ = p.Length;                                                   // the first lookup after a change may build the starts
            long before = p.Runs.ElementReads;
            int hits = 0;
            for (int i = 0; i < 2000; i++)
            {
                int offset = (i * 7919) % (runs * 2);
                if (p.RunBefore(offset, out _) != null) hits++;
                if (p.RunAfter(offset, out _) != null) hits++;
                hits += p.Length > 0 ? 1 : 0;
            }
            long reads = p.Runs.ElementReads - before;
            Assert.True(hits > 0);
            Assert.True(reads <= 2 * 2000 + 10, $"{reads} runs read for 2000 lookups");
        }

        [Fact]
        public void Asking_whether_a_point_takes_typing_reads_a_few_runs_however_long_the_paragraph_is()
        {
            const int runs = 4000;
            var e = new DocumentEditor(Doc(Many(runs)));
            var runList = e.Document.Paragraphs[0].Runs;
            e.IsEditableAt(new TextPosition(0, 1));
            long before = runList.ElementReads;
            for (int i = 0; i < 1000; i++) e.IsEditableAt(new TextPosition(0, (i * 37) % (runs * 2)));
            long reads = runList.ElementReads - before;
            Assert.True(reads <= 1000 * 4 + 10, $"{reads} runs read for 1000 questions");
        }

        [Fact]
        public void The_nearest_editable_point_beside_read_only_runs_is_found_without_a_scan_from_the_start_of_the_paragraph()
        {
            const int runs = 4000;
            var e = new DocumentEditor(Doc(Many(runs)));                    // even runs are read-only, odd runs editable
            var runList = e.Document.Paragraphs[0].Runs;
            e.NearestEditable(new TextPosition(0, 5000));                   // builds whatever the first lookup builds
            long before = runList.ElementReads;
            for (int i = 0; i < 500; i++)
            {
                int readOnlyRun = 2 * ((i * 3) % (runs / 2 - 1)) + 2;        // even index: a read-only run in the middle
                var found = e.NearestEditable(new TextPosition(0, readOnlyRun * 2 + 1));
                Assert.True(e.IsEditableAt(found));
            }
            long reads = runList.ElementReads - before;
            Assert.True(reads <= 500 * 30, $"{reads} runs read for 500 searches");
        }
#endif

        // ---- NearestEditable against a plain walk over the insertion points

        static Run Kind(int kind, string text)
        {
            var r = new Run { Ws = "en", Text = text };
            if (kind == 1) r.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            if (kind == 2) r.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableSemi);
            return r;
        }

        static Paragraph RandomEditability(Random rng)
        {
            var p = new Paragraph { DefaultWs = "en" };
            int runs = rng.Next(1, 9);
            for (int i = 0; i < runs; i++)
            {
                int what = rng.Next(8);
                if (what == 0) p.Runs.Add(Kind(rng.Next(3), string.Empty));
                else if (what == 1) { var o = Run.Object("en", "o"); if (rng.Next(2) == 0) o.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo); p.Runs.Add(o); }
                else if (what == 2 && i == runs - 1) p.Runs.Add(Kind(rng.Next(3), DocumentEditor.LineSeparator.ToString()));
                else p.Runs.Add(Kind(rng.Next(3), new string('a', rng.Next(1, 5))));
            }
            return p;
        }

        // Whether an insertion point takes typing, from the runs alone: the paragraph is empty, or the run before the point may be
        // edited, or the run after it may and the text there is not hidden by the limit.
        static bool Takes(Paragraph p, int offset, int limit)
        {
            if (OldLength(p) == 0) return true;
            var before = OldBefore(p, offset, out _);
            var after = OldAfter(p, offset, out _);
            if (before != null && before.Editable != RunEditable.NotEditable) return true;
            if (after == null || after.Editable == RunEditable.NotEditable) return false;
            var runs = Snapshot(p);
            string last = runs[runs.Count - 1].Text;
            bool loneBreak = last.Length > 0 && last[last.Length - 1] == DocumentEditor.LineSeparator && OldLength(p) == limit + 1;
            bool hidden = offset > limit || (offset == limit && !loneBreak);
            return !hidden;
        }

        static int Oracle(Paragraph p, int at, int direction, int limit)
        {
            int visible = Math.Min(OldLength(p), limit);
            if (Takes(p, at, limit)) return at;
            int fwd = -1, back = -1;
            for (int o = at + 1; o <= visible && fwd < 0; o++) if (Takes(p, o, limit)) fwd = o;
            for (int o = at - 1; o >= 0 && back < 0; o--) if (Takes(p, o, limit)) back = o;
            if (fwd < 0 && back < 0) return at;
            if (fwd < 0) return back;
            if (back < 0) return fwd;
            if (direction > 0) return fwd;
            if (direction < 0) return back;
            return at - back <= fwd - at ? back : fwd;
        }

        [Fact]
        public void The_nearest_editable_point_is_the_one_a_plain_walk_over_the_insertion_points_finds()
        {
            var rng = new Random(78);
            int checkedPoints = 0;
            for (int round = 0; round < 300; round++)
            {
                var p = RandomEditability(rng);
                int len = OldLength(p);
                int? cut = rng.Next(3) == 0 ? rng.Next(0, len + 1) : (int?)null;
                var e = new DocumentEditor(Doc(p));
                if (cut != null) e.CaretLimit = _ => cut.Value;
                int limit = cut ?? len;
                for (int at = 0; at <= Math.Min(len, limit); at++)
                    foreach (int direction in new[] { 0, 1, -1 })
                    {
                        int want = Oracle(p, at, direction, limit);
                        var got = e.NearestEditable(new TextPosition(0, at), direction);
                        Assert.True(want == got.Offset, "round " + round + ", at " + at + ", direction " + direction + ", limit " + limit + ": expected " + want + ", got " + got.Offset + ", runs [" + string.Join("|", Snapshot(p).Select(r => (r.IsObject ? "O" : r.Text) + ":" + r.Editable)) + "]");
                        checkedPoints++;
                    }
            }
            Assert.True(checkedPoints > 1000);
        }

        // ---- Slice looks at the runs the range touches

        static List<Run> OldSlice(Paragraph p, int start, int end)
        {
            var c = p.Clone();
            var result = new List<Run>();
            int a = c.SplitRunAt(start);
            int b = c.SplitRunAt(end);
            for (int i = a; i < b; i++) result.Add(c.Runs[i].Clone());
            return result;
        }

        [Fact]
        public void Slice_gives_the_runs_a_clone_and_split_gave_for_every_range_of_random_paragraphs()
        {
            var rng = new Random(79);
            for (int round = 0; round < 120; round++)
            {
                var p = Random(rng, rng.Next(0, 9));
                int len = OldLength(p);
                for (int start = 0; start <= len; start++)
                    for (int end = start; end <= len + 1; end++)
                    {
                        var want = OldSlice(p, start, end);
                        var got = p.Slice(start, end);
                        Assert.True(want.Count == got.Count, "round " + round + " [" + start + "," + end + ") expected " + want.Count + " runs, got " + got.Count);
                        for (int k = 0; k < want.Count; k++)
                        {
                            Assert.Equal(want[k].Text, got[k].Text);
                            Assert.Equal(want[k].Ws, got[k].Ws);
                            Assert.Equal(want[k].ObjData, got[k].ObjData);
                            Assert.True(want[k].Props.ContentEquals(got[k].Props));
                        }
                    }
            }
        }

#if TEXTEDITING_COUNTERS
        [Fact]
        public void Slicing_a_long_paragraph_reads_the_runs_the_range_touches()
        {
            var p = Many(4000);
            _ = p.Length;
            long before = p.Runs.ElementReads;
            for (int i = 0; i < 500; i++) p.Slice(i * 7, i * 7 + 9);
            Assert.True(p.Runs.ElementReads - before <= 500 * 8, (p.Runs.ElementReads - before) + " runs read for 500 slices");
        }
#endif

        // ---- a run is held by the paragraph that holds it and by no other

        static void AssertEveryRunHasOneOwner(Document d)
        {
            foreach (var p in d.Paragraphs)
                foreach (var r in Snapshot(p))
                    Assert.Equal(1, r.OwnerCount);
        }

        [Fact]
        public void After_every_kind_of_edit_each_run_of_the_document_is_held_by_exactly_one_paragraph_list()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one two", "fr", " trois"), Para("en", "en", "second")));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 3)); e.TypeText("xyz"); AssertEveryRunHasOneOwner(e.Document);
            e.Enter(); AssertEveryRunHasOneOwner(e.Document);
            e.Backspace(); AssertEveryRunHasOneOwner(e.Document);
            e.Paste(new List<Paragraph> { Para("en", "en", "pasted", "de", "more"), Para("en", "en", "tail") }); AssertEveryRunHasOneOwner(e.Document);
            e.ReplaceAll(new FindOptions { Pattern = "pasted" }, "swapped"); AssertEveryRunHasOneOwner(e.Document);
            undo.Undo(); AssertEveryRunHasOneOwner(e.Document);
            undo.Undo(); AssertEveryRunHasOneOwner(e.Document);
            undo.Redo(); AssertEveryRunHasOneOwner(e.Document);
            e.ExternalReplaceParagraph(0, Para("en", "en", "external", "fr", " text"), 0, 3, 3); AssertEveryRunHasOneOwner(e.Document);
            e.SingleParagraph = true;
            e.SetCaret(new TextPosition(0, 0)); e.PasteText("a\tb\nc"); AssertEveryRunHasOneOwner(e.Document);
            foreach (var p in e.Document.Paragraphs) p.Runs = new RunList(Snapshot(p).Select(r => r.Clone()));
            AssertEveryRunHasOneOwner(e.Document);
        }

        [Fact]
        public void A_list_that_is_built_and_dropped_or_replaced_leaves_no_link_on_its_runs()
        {
            var r = new Run { Ws = "en", Text = "abc" };
            var thrownAway = new RunList(new[] { r });
            Assert.Equal(0, r.OwnerCount);
            var p = new Paragraph(); p.Runs.Add(r);
            Assert.Equal(1, r.OwnerCount);
            p.Runs = new RunList(new[] { new Run { Ws = "en", Text = "other" } });
            Assert.Equal(0, r.OwnerCount);
            p.Runs.Add(r);
            p.Runs.Clear();
            Assert.Equal(0, r.OwnerCount);
            var shared = new Paragraph(); shared.Runs.Add(r);
            var weak = CreateAndDropAParagraphHolding(r);
            Assert.Equal(2, r.OwnerCount);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(weak.TryGetTarget(out _), "a dropped paragraph is still held by its run");
            r.Text = "changed";                                              // prunes the dead link
            Assert.Equal(1, r.OwnerCount);
            GC.KeepAlive(shared);
            GC.KeepAlive(thrownAway);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference<Paragraph> CreateAndDropAParagraphHolding(Run r)
        {
            var temp = new Paragraph();
            temp.Runs.Add(r);
            return new WeakReference<Paragraph>(temp);
        }
    }
}
