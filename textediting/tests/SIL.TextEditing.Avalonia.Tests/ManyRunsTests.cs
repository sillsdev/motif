using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A paragraph of tens of thousands of runs: the work a layout and a draw do grows with the runs, not with their square, and runs that format alike are
    /// one stretch of text to the formatter. Counted, not timed (issue 52).</summary>
    public class ManyRunsTests
    {
        static Paragraph Many(int runs, bool decorated)
        {
            var p = Para("en");
            for (int i = 0; i < runs; i++)
            {
                var r = new Run { Ws = "en", Text = i % 4 == 0 ? "word. " : "w " };
                if (i % 2 == 0) r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                if (decorated)
                {
                    if (i % 3 == 0) r.Props.SetInt(TextPropertyNumbers.Offset, 2000, TextPropertyNumbers.VariationMilliPoint);
                    if (i % 3 == 1) r.Props.SetInt(TextPropertyNumbers.BackColor, 0x99FFFF);
                    if (i % 3 == 2) r.Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle);
                }
                p.Runs.Add(r);
            }
            return p;
        }

        static long Probes(int runs)
        {
            long probes = 0;
            Headless.Run(() =>
            {
            using var probe = LayoutProbe.Start();
            var field = Fixtures.Field(Doc(Many(runs, true)), 500);
            Fixtures.Render(field);
            probes = probe.RangeProbes;
            });
            return probes;
        }

        [Fact]
        public void The_ranges_looked_at_by_a_layout_and_a_draw_grow_with_the_runs_not_with_their_square()
        {
            long small = Probes(1000), large = Probes(4000);
            Assert.True(small > 0);
            Assert.True(large < small * 8, "ranges looked at: " + small + " for 1000 runs, " + large + " for 4000");   // four times the runs: about four times the work, not sixteen
        }

        [Fact]
        public void Runs_that_format_alike_are_one_stretch_of_text_to_the_formatter() => Headless.Run(() =>
        {
            var p = Para("en");
            for (int i = 0; i < 600; i++)
            {
                var r = new Run { Ws = "en", Text = "word " };
                r.Props.SetInt(999, i);   // a property that is carried and not drawn: the runs differ, the look does not
                p.Runs.Add(r);
            }
            var pl = Fixtures.Field(Doc(p), 400).ParagraphLayoutFor(0);
            int slots = pl.Lines.Sum(l => l.AllSlots.Count);
            Assert.True(slots <= pl.Lines.Count * 2, "run slots: " + slots + " for " + pl.Lines.Count + " lines");
        });
    }
}
