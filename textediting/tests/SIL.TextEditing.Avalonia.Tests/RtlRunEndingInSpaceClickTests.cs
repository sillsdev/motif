using System;
using Avalonia;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>In a left-to-right line with a right-to-left run that ends in spaces, a click at the x of a caret stop gives that stop, not the end of the
    /// right-to-left run beside it, a space or two to the right (issue 109). At the x where the run before ends the right-to-left run's own end is too, and the layout's
    /// answer there is the run's, which is another offset drawn somewhere else.</summary>
    public class RtlRunEndingInSpaceClickTests
    {
        [Theory]
        [MemberData(nameof(ClickCorpus.BoundaryNumbers), MemberType = typeof(ClickCorpus))]
        public void A_click_at_the_x_of_a_stop_beside_a_right_to_left_run_that_ends_in_spaces_gives_that_stop(int index) => Headless.Run(() =>
        {
            var c = ClickCorpus.BoundaryCases[index];
            var pl = c.Layout();
            int checkedStops = 0;
            foreach (var li in pl.Lines)
            {
                if (li.IsEmpty) continue;
                foreach (var stop in pl.VisualStops(li))
                {
                    var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                    var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                    Assert.True(Math.Abs(caret.X - stop.X) < 1, c + ": a click at " + stop.X + " (the stop of offset " + stop.Offset + ") gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                    checkedStops++;
                }
            }
            Assert.True(checkedStops > 20);
        });
    }
}
