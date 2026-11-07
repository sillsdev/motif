using System;
using System.Linq;
using Avalonia;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A caret that the control holds at its edge (the text reaches past it, as a justified or right-aligned line does) is drawn at x = width less a
    /// caret on the right and at 0 on the left, not where the line's layout has it: a click at the x of such a caret gives that caret, not the one the layout has
    /// at that x, which can be a letter's width away.</summary>
    public class HeldCaretClickTests
    {
        [Theory]
        [MemberData(nameof(ClickCorpus.EdgeNumbers), MemberType = typeof(ClickCorpus))]
        public void A_click_at_a_caret_held_at_the_edge_of_the_control_gives_that_caret_in_a_line_with_several_runs_of_both_directions(int index) => Headless.Run(() =>
        {
            var c = ClickCorpus.EdgeCases[index];
            var pl = c.Layout();
            int held = 0;
            foreach (var li in pl.Lines)
            {
                if (li.IsEmpty) continue;
                int core = pl.TextEnd(li);
                while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                foreach (var stop in pl.VisualStops(li))
                {
                    if (stop.Offset > core || Math.Abs(pl.CaretXBeforeClamp(li, stop.Hit) - stop.X) < 0.01) continue;   // not a held stop, or one of the spaces the line ends with
                    var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                    var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                    Assert.True(Math.Abs(caret.X - stop.X) < 1, c + ": a click at " + stop.X + " (the stop of offset " + stop.Offset + ", held at the edge) gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                    held++;
                }
            }
            Assert.True(held > 0, c + ": no stop is held at the edge");
        });

        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        public static readonly object[][] SideCases =
        {
            new object[] { English, false, 3, false },   // right aligned: the text ends at the right edge
            new object[] { English, false, 5, false },
            new object[] { Arabic, true, 1, true },      // left aligned: the text starts at the left edge
            new object[] { Arabic, true, 5, true },
        };

        [Theory]
        [MemberData(nameof(SideCases))]
        public void A_click_at_a_caret_held_at_the_right_or_the_left_edge_of_the_control_gives_that_caret(string text, bool rtl, int align, bool leftEdge) => Headless.Run(() =>
        {
            int held = 0;
            for (double width = 150; width <= 300; width += 0.5)
            {
                var p = SIL.TextEditing.Tests.TestDocs.Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
                p.Rtl = rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                var pl = Fixtures.Field(SIL.TextEditing.Tests.TestDocs.Doc(p), width).ParagraphLayoutFor(0);
                foreach (var li in pl.Lines)
                {
                    int core = pl.TextEnd(li);
                    while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                    foreach (var stop in pl.VisualStops(li))
                    {
                        if (stop.Offset > core || (leftEdge ? stop.X > 0.01 : stop.X < width - ParagraphLayout.CaretWidth - 0.01)) continue;
                        var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                        var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                        Assert.True(Math.Abs(caret.X - stop.X) < 1, "width " + width + ": a click at " + stop.X + " (the stop of offset " + stop.Offset + ") gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                        held++;
                    }
                }
            }
            Assert.True(held > 20, "stops at the edge pinned: " + held);
        });
    }
}
