using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>
    /// Visual caret stops, hit testing and selection rectangles over mixed left-to-right and right-to-left
    /// text, with expected values derived by hand from the bidi algorithm. Two Avalonia faults are pinned here as known
    /// failures so a fix shows up as a failing test (and the workaround can go): hit testing in a right-to-left
    /// paragraph on Avalonia 11.3 (the layout reads such a line from its runs), and inline objects in right-to-left text on both generations.
    /// </summary>
    public class BidiTests
    {
        const string Text = "ab عربي cd"; // ab, four Arabic letters, cd

        sealed class Case
        {
            public string Name = "";
            public bool RtlParagraph;
            public int[] Visual = Array.Empty<int>();   // logical cluster index at each visual position, left to right
            public bool[] RtlChar = Array.Empty<bool>();
        }

        static readonly Case Ltr = new Case
        {
            Name = "LTR paragraph",
            RtlParagraph = false,
            Visual = new[] { 0, 1, 2, 6, 5, 4, 3, 7, 8, 9 },
            RtlChar = new[] { false, false, false, true, true, true, true, false, false, false },
        };

        static readonly Case Rtl = new Case
        {
            Name = "RTL paragraph",
            RtlParagraph = true,
            Visual = new[] { 8, 9, 7, 6, 5, 4, 3, 2, 0, 1 },
            RtlChar = new[] { false, false, true, true, true, true, true, true, false, false },
        };

        static RichTextField Build(Case c)
        {
            var p = Para("en", "en", Text);
            p.Rtl = c.RtlParagraph;
            return Fixtures.Field(Doc(p), 520);
        }

        /// <summary>Offsets allowed at each of the n+1 visual positions, left to right.</summary>
        static List<HashSet<int>> Allowed(Case c)
        {
            int n = c.Visual.Length;
            var allowed = new List<HashSet<int>>();
            for (int k = 0; k <= n; k++)
            {
                var set = new HashSet<int>();
                if (k > 0) { int u = c.Visual[k - 1]; set.Add(c.RtlChar[u] ? u : u + 1); }
                if (k < n) { int v = c.Visual[k]; set.Add(c.RtlChar[v] ? v + 1 : v); }
                allowed.Add(set);
            }
            return allowed;
        }

        static List<string> HitTestMismatches(RichTextField field, Case c, List<ParagraphLayout.Stop> stops)
        {
            var li = field.ParagraphLayoutFor(0).Lines[0];
            var bad = new List<string>();
            for (int k = 0; k < c.Visual.Length; k++)
            {
                int i = c.Visual[k];
                double xl = stops[k].X, xr = stops[k + 1].X, y = li.Line.Height / 2;
                int leftExpected = c.RtlChar[i] ? i + 1 : i, rightExpected = c.RtlChar[i] ? i : i + 1;
                var pl = field.PointToPosition(new Point(xl + (xr - xl) * 0.25, y));
                var pr = field.PointToPosition(new Point(xl + (xr - xl) * 0.75, y));
                if (pl.Offset != leftExpected) bad.Add("cluster " + i + " left got " + pl.Offset + " expected " + leftExpected);
                if (pr.Offset != rightExpected) bad.Add("cluster " + i + " right got " + pr.Offset + " expected " + rightExpected);
            }
            return bad;
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "ltr" };
            yield return new object[] { "rtl" };
        }

        static Case ByName(string n) => n == "ltr" ? Ltr : Rtl;

        [Theory, MemberData(nameof(Cases))]
        public void Caret_stops_are_the_visual_positions_left_to_right(string which) => Headless.Run(() =>
        {
            var c = ByName(which);
            var field = Build(c);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            var allowed = Allowed(c);
            Assert.Equal(c.Visual.Length + 1, stops.Count);
            for (int k = 0; k < stops.Count; k++)
                Assert.True(allowed[k].Contains(stops[k].Offset), c.Name + " stop " + k + " offset " + stops[k].Offset + " not in {" + string.Join("|", allowed[k]) + "}");
        });

        [Fact]
        public void Hit_testing_in_a_left_to_right_paragraph_is_exact() => Headless.Run(() =>
        {
            var field = Build(Ltr);
            var layout = field.ParagraphLayoutFor(0);
            Assert.Empty(HitTestMismatches(field, Ltr, layout.VisualStops(layout.Lines[0])));
        });

        [Fact]
        public void Hit_testing_in_a_right_to_left_paragraph_is_exact_on_both_avalonia_lines() => Headless.Run(() =>
        {
            var field = Build(Rtl);
            var layout = field.ParagraphLayoutFor(0);
            Assert.Empty(HitTestMismatches(field, Rtl, layout.VisualStops(layout.Lines[0])));
        });

        [Fact]
        public void Avalonia_11_3_hit_testing_in_a_right_to_left_paragraph_is_wrong_so_the_layout_reads_the_runs_instead() => Headless.Run(() =>
        {
            var field = Build(Rtl);
            var layout = field.ParagraphLayoutFor(0);
            var li = layout.Lines[0];
            Assert.True(li.UsesRuns, "the layout reads this line from its runs");
            var stops = layout.VisualStops(li);
            int wrong = 0;
            for (int k = 0; k < Rtl.Visual.Length; k++)
            {
                int i = Rtl.Visual[k];
                double xl = stops[k].X, xr = stops[k + 1].X;
                var left = li.Line.GetCharacterHitFromDistance(xl + (xr - xl) * 0.25);
                var right = li.Line.GetCharacterHitFromDistance(xl + (xr - xl) * 0.75);
                if (left.FirstCharacterIndex + left.TrailingLength != (Rtl.RtlChar[i] ? i + 1 : i)) wrong++;
                if (right.FirstCharacterIndex + right.TrailingLength != (Rtl.RtlChar[i] ? i : i + 1)) wrong++;
            }
            // Known Avalonia 11.3.17 fault: a cluster of Latin letters inside the Arabic run maps to offsets +2 (4 of 20 wrong in the spike).
            // Fixed in 12. When 11.3 goes (ADR 0009), the layout's reading of the runs for this fault goes with this test.
            if (Headless.AvaloniaMajor >= 12) Assert.Equal(0, wrong);
            else Assert.NotEqual(0, wrong);
        });

        [Theory, MemberData(nameof(Cases))]
        public void Selection_rectangles_cover_the_visual_groups_of_a_logical_range(string which) => Headless.Run(() =>
        {
            var c = ByName(which);
            var field = Build(c);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            foreach (var range in new[] { new[] { 2, 5 }, new[] { 3, 7 }, new[] { 0, 10 }, new[] { 6, 9 } })
            {
                var rects = layout.SelectionRects(range[0], range[1]);
                var visual = c.Visual.Select((ci, k) => new { ci, k }).Where(t => t.ci >= range[0] && t.ci < range[1]).Select(t => t.k).ToList();
                int groups = 0;
                for (int q = 0; q < visual.Count; q++) if (q == 0 || visual[q] != visual[q - 1] + 1) groups++;
                double expectedWidth = visual.Sum(k => stops[k + 1].X - stops[k].X);
                var sorted = rects.OrderBy(r => r.X).ToList();
                int merged = 0;
                for (int q = 0; q < sorted.Count; q++) if (q == 0 || sorted[q].X > sorted[q - 1].Right + 0.5) merged++;
                Assert.True(merged == groups, c.Name + " [" + range[0] + "," + range[1] + ") groups " + merged + " expected " + groups);
                // (a stop at the edge of the control is held inside it, which can take a caret's width off the first or the last box)
                Assert.True(Math.Abs(expectedWidth - rects.Sum(r => r.Width)) < 1.5 + ParagraphLayout.CaretWidth, c.Name + " [" + range[0] + "," + range[1] + ") width");
            }
        });

        static List<int> OrcStopOffsets(bool glyph, out int hitMismatches)
        {
            var p = new Paragraph { Rtl = true, DefaultWs = "ar" };
            p.Runs.Add(new Run { Ws = "ar", Text = "مرحبا" });
            p.Runs.Add(Run.Object("ar", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "ar", Text = "بال" });
            var field = Fixtures.Field(Doc(p), 520, f => { f.ObjectAsGlyph = glyph; f.ObjectImage = Fixtures.Picture; });
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            hitMismatches = 0;
            for (int k = 0; k + 1 < stops.Count; k++)
            {
                var pos = field.PointToPosition(new Point((stops[k].X + stops[k + 1].X) / 2, 5));
                if (pos.Offset != Math.Max(stops[k].Offset, stops[k + 1].Offset) && pos.Offset != Math.Min(stops[k].Offset, stops[k + 1].Offset)) hitMismatches++;
            }
            return stops.Select(s => s.Offset).ToList();
        }

        static bool StrictlyDescending(List<int> offsets) => offsets.Count == 10 && offsets.Zip(offsets.Skip(1), (a, b) => a > b).All(x => x);

        [Fact]
        public void Object_in_right_to_left_text_with_the_em_space_workaround_has_ordered_stops() => Headless.Run(() =>
        {
            var offsets = OrcStopOffsets(true, out int bad);
            Assert.True(StrictlyDescending(offsets), string.Join(" ", offsets));
            Assert.Equal(0, bad);
        });

        [Fact]
        public void Object_in_right_to_left_text_as_a_drawable_run_is_a_known_avalonia_fault() => Headless.Run(() =>
        {
            // Avalonia issue 21792: stops by x come out 9 8 6 7 5 4 3 2 1 0, not monotonic. Present in 11.3.17 and 12.1.2.
            // If this fails, Avalonia fixed it: make ObjectAsGlyph default off and delete the workaround.
            var offsets = OrcStopOffsets(false, out _);
            Assert.False(StrictlyDescending(offsets), string.Join(" ", offsets));
        });

        public static IEnumerable<object[]> MixedLines()
        {
            foreach (var text in new[]
            {
                "ab عربي cd",
                "ab عربي cd عربي ef",
                "The quick عربي fox (جمل) jumps over the lazy dog while the five boxing wizards jump quickly",
                "هذا نص عربي with some English words وكلمات عربية أخرى in the middle of the line and نهاية الفقرة هنا جملة طويلة",
            })
                foreach (var rtl in new[] { false, true })
                    foreach (var width in new[] { 200.0, 300, 520 })
                        yield return new object[] { text, rtl, width };
        }

        [Theory, MemberData(nameof(MixedLines))]
        public void Hit_testing_at_the_x_of_a_stop_gives_a_caret_at_that_stop_whichever_of_the_offsets_that_place_has_it_returns(string text, bool rtl, double width) => Headless.Run(() =>
        {
            // At a boundary between directions one place on the screen is two offsets (after the last character of one run, before the first
            // of the next), so a click there may answer with either; the caret it gives (offset with its hit) must be at the click.
            var p = Para("en", "en", text);
            p.Rtl = rtl;
            var layout = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
            foreach (var li in layout.Lines)
                foreach (var stop in layout.VisualStops(li))
                {
                    if (stop.X > width - ParagraphLayout.CaretWidth - 0.01) continue;   // held inside the control: not where the character edge is
                    var hit = layout.HitTestDetailed(new Point(stop.X, li.Y + 2));
                    var caret = layout.CaretRect(hit.Offset, false, hit.CaretHit);
                    Assert.True(Math.Abs(caret.X - stop.X) < 1, "a click at " + stop.X + " (the stop of offset " + stop.Offset + ") gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                }
        });
    }
}
