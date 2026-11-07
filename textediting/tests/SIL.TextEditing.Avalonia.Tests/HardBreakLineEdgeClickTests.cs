using System;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A click beyond the end of a line that ends in a hard line break puts the caret at the line's visual end, in the paragraph's direction,
    /// which is not the logical end when the line ends in a run of the other direction. As in FieldWorks, which keeps the visual edge.</summary>
    public class HardBreakLineEdgeClickTests
    {
        static readonly string Break = ((char)0x2028).ToString();

        static ParagraphLayout Layout(string text, bool rtl, double width)
        {
            var p = Para("en", "en", text);
            p.Rtl = rtl;
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        public static readonly object[][] Cases =
        {
            new object[] { "abc عربي" + Break + "def", false },
            new object[] { "abc عربي" + Break + "def", true },
            new object[] { "عربي abc" + Break + "def", false },
            new object[] { "عربي abc" + Break + "def", true },
            new object[] { "The quick عربي fox (جمل) jumps" + Break + "over the lazy" + Break + "dog عربية", false },
            new object[] { "هذا نص عربي with some English words وكلمات" + Break + "عربية أخرى in the middle" + Break + "نهاية", true },
            new object[] { "ab عربي cd عربي" + Break + "ef", false },
            new object[] { "ab عربي cd عربي" + Break + "ef", true },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_click_beyond_the_end_of_a_line_that_ends_in_a_hard_break_gives_the_caret_at_the_lines_visual_end(string text, bool rtl) => Headless.Run(() =>
        {
            int checkedLines = 0;
            foreach (double width in new[] { 120.0, 200, 300, 520 })
            {
                var pl = Layout(text, rtl, width);
                foreach (var li in pl.Lines)
                {
                    if (li.IsEmpty || li.End <= li.Start || pl.EffectiveText[li.End - 1] != (char)0x2028) continue;
                    var stops = pl.VisualStops(li);
                    // The break stands at the end of the line in the paragraph's direction: the edge a click beyond it is at.
                    var edge = rtl ? stops.First() : stops.Last();
                    // Beyond the break, and at the stop itself (where the break's own place is: the click is on it).
                    foreach (double clickX in new[] { rtl ? -10 : width + 10, edge.X })
                    {
                        var hit = pl.HitTestDetailed(new Point(clickX, li.Y + 2));
                        var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                        Assert.True(Math.Abs(caret.X - edge.X) < 1, "width " + width + ", a click at " + clickX + " on the line at offset " + li.Start + " gave offset " + hit.Offset + " (caret " + caret.X + "), the visual end is offset " + edge.Offset + " at " + edge.X);
                        checkedLines++;
                    }
                }
            }
            Assert.True(checkedLines >= 8, "lines checked: " + checkedLines);
        });
    }
}
