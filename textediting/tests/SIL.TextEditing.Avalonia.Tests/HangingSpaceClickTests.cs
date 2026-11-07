using System;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The spaces a wrapped line ends with hang past its text: a click at the x of one of their carets gives that caret (also where the control holds
    /// it inside its edge, over the letters before the spaces), and a click beyond the End caret gives the End caret, the end of the line, never one space short of it.</summary>
    public class HangingSpaceClickTests
    {
        static int Core(ParagraphLayout pl, LineInfo li)
        {
            int core = pl.TextEnd(li);
            while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
            return core;
        }

        [Theory]
        [MemberData(nameof(ClickCorpus.HangingNumbers), MemberType = typeof(ClickCorpus))]
        public void A_click_at_the_caret_of_a_hanging_space_gives_that_caret_in_a_line_with_several_runs_of_both_directions(int index) => Headless.Run(() =>
        {
            var c = ClickCorpus.HangingCases[index];
            var pl = c.Layout();
            int checkedStops = 0;
            foreach (var li in pl.Lines)
            {
                if (li.IsEmpty) continue;
                int core = Core(pl, li);
                foreach (var stop in pl.VisualStops(li))
                {
                    if (stop.Offset <= core) continue;
                    var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                    var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                    Assert.True(Math.Abs(caret.X - stop.X) < 1, c + ": a click at " + stop.X + " (the stop of offset " + stop.Offset + " among the spaces after " + core + ") gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                    checkedStops++;
                }
            }
            Assert.True(checkedStops > 0);
        });

        [Theory]
        [MemberData(nameof(ClickCorpus.BeyondNumbers), MemberType = typeof(ClickCorpus))]
        public void A_click_beyond_the_End_caret_of_a_wrapped_line_gives_the_end_of_the_line(int index) => Headless.Run(() =>
        {
            var c = ClickCorpus.BeyondCases[index];
            var pl = c.Layout();
            int checkedLines = 0;
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int core = Core(pl, li), end = pl.LineEnd(i, out var endHit);
                if (core == end || core == li.Start) continue;
                double endX = pl.CaretRect(end, false, endHit).X;
                for (double dx = 0.5; dx < 60; dx += 2.3)
                {
                    double x = c.Rtl ? endX - dx : endX + dx;
                    if (x < 0 || x > c.Width) continue;
                    var hit = pl.HitTestDetailed(new Point(x, li.Y + 2));
                    Assert.True(hit.Offset == end, c + ": line " + i + " ends at " + end + " (caret at " + endX + "), a click at " + x + " gave offset " + hit.Offset);
                }
                checkedLines++;
            }
            Assert.True(checkedLines > 0);
        });
    }
}
