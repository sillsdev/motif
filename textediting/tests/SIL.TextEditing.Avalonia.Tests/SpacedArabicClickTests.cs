using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A click at the x of a caret stop gives a caret at that stop, in a line of Arabic words with three spaces between them, wherever the
    /// spaces fall: inside the line, or hanging past its text at a wrap (where Avalonia 12 gives the leading edge of the last space for a click at the
    /// end of the line, one space off).</summary>
    public class SpacedArabicClickTests
    {
        const string Arabic = "هذه   فقرة   عربية   طويلة   بما   يكفي   لتلتف   على   عدة   أسطر   عندما   تكون   النافذة   ضيقة   وتبين   أين   تقع";
        const string Digits = "12 فقرة   طويلة عربية   12 ab (جمل)   كلمات   12   هذه   فقرة   dog   عربي   طويلة   12   (جمل)   ab";
        const string Mixed = "dog   عربي   طويلة   fox   (جمل)   12   عربية   ab   كلمات   فقرة   هذه   عربي   dog   فقرة";

        public static readonly object[][] Cases =
        {
            new object[] { Arabic, false, 0 },
            new object[] { Arabic, false, 2 },
            new object[] { Mixed, false, 0 },
            new object[] { Mixed, false, 2 },
            new object[] { Digits, false, 0 },
            new object[] { Digits, false, 2 },
            new object[] { Arabic, true, 0 },
            new object[] { Mixed, true, 0 },
            new object[] { Digits, true, 0 },
            new object[] { "12 فقرة   طويلة عربية   12 ab (جمل) ", false, 2 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_click_at_the_x_of_a_stop_gives_a_caret_there_where_the_spaces_between_arabic_words_are_three(string text, bool rtl, int align) => Headless.Run(() =>
        {
            int checkedStops = 0, hanging = 0;
            for (double width = 120; width <= 420; width += 1)
            {
                var p = Para("en", "en", text + " ");
                p.Rtl = rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                foreach (var li in pl.Lines)
                {
                    if (li.IsEmpty) continue;
                    // A line that wraps ends with spaces that hang past its text: a click at the end of the last of them (the End caret) is at the
                    // end of the line, not one space short of it.
                    int core = pl.TextEnd(li), end = core;
                    while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                    if (!rtl && li != pl.Lines[pl.Lines.Count - 1] && end - core >= 2 && li.Line.WidthIncludingTrailingWhitespace <= pl.ContentWidth)   // the spaces fit the text area
                    {
                        var stops = pl.VisualStops(li);
                        var endStop = stops.Where(s => s.Offset == end).ToList();
                        Assert.True(endStop.Count > 0, "width " + width + ": the line that ends at " + end + " has no End caret stop");   // Avalonia 12 gave none without the correction
                        double ex = endStop.Last().X;   // where the End caret is drawn
                        if (ex >= 0 && ex <= width - ParagraphLayout.CaretWidth)
                        {
                            var clicked = pl.HitTestDetailed(new Point(ex, li.Y + 2));
                            Assert.True(clicked.Offset == end, "width " + width + ": a click at " + ex + " (the End caret of a line that ends at " + end + ") gave offset " + clicked.Offset);
                            for (int o = core + 1; o <= end; o++)
                            {
                                var mid = stops.Where(s => s.Offset == o).ToList();
                                Assert.True(mid.Count > 0, "width " + width + ": no caret stop at " + o + " among the hanging spaces of the line that ends at " + end);
                                double mx = pl.CaretXBeforeClamp(li, new global::Avalonia.Media.CharacterHit(o - 1, 1));
                                if (Math.Abs(mid[0].X - mx) > 0.01) continue;
                                Assert.True(pl.HitTestDetailed(new Point(mx, li.Y + 2)).Offset == o, "width " + width + ": a click after the hanging space at " + (o - 1) + " did not give offset " + o);
                            }
                            hanging++;
                        }
                    }
                    foreach (var stop in pl.VisualStops(li))
                    {
                        // Where the line puts the stop, before the control's edge holds the caret inside it: a click is made there.
                        double x = pl.CaretXBeforeClamp(li, stop.Hit);
                        if (Math.Abs(stop.X - x) > 0.01) continue;   // held inside the control: LineEndCaretTests clicks there
                        var hit = pl.HitTestDetailed(new Point(x, li.Y + 2));
                        double got = pl.CaretXBeforeClamp(li, hit.CaretHit);
                        Assert.True(Math.Abs(got - x) < 1, "width " + width + ": a click at " + x + " (the stop of offset " + stop.Offset + ") gave offset " + hit.Offset + ", whose caret is at " + got);
                        checkedStops++;
                    }
                }
            }
            Assert.True(checkedStops > 1000, "stops checked: " + checkedStops);
            Assert.True(hanging > 0 || rtl, "lines with hanging spaces checked: " + hanging);
        });
    }
}
