using System.Linq;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A run's background colour is painted under its text, not under the space a wrapped line ends with (that space hangs outside
    /// the line's text, so painting it leaves a coloured sliver past the end).</summary>
    public class RunBackgroundTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        static bool IsYellow(Color c) => c.R > 200 && c.G > 200 && c.B < 100;

        public static readonly object[][] Cases =
        {
            new object[] { English, false, 0 },
            new object[] { English, false, 5 },
            new object[] { Arabic, true, 0 },
            new object[] { Arabic, true, 5 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void No_background_is_painted_in_the_space_a_wrapped_line_ends_with(string text, bool rtl, int align) => Headless.Run(() =>
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            p.Runs[0].Props.SetInt(TextPropertyNumbers.BackColor, 0x00FFFF);   // BGR: yellow
            p.Props.SetInt(TextPropertyNumbers.TrailingIndent, 80000, 1);   // room beside the text area, so a space hanging past it can be seen
            var field = Fixtures.Field(Doc(p), 400);
            var pl = field.ParagraphLayoutFor(0);
            var shot = Fixtures.Render(field);
            Assert.True(pl.Lines.Count >= 3);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int core = pl.TextEnd(li);
                Assert.Equal(' ', pl.EffectiveText[core - 1]);   // every line but the last wraps at a space here
                while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                var rects = pl.SelectionRects(li.Start, core);
                double edge = rtl ? rects.Min(r => r.X) : rects.Max(r => r.Right);
                int y = (int)(li.TextY + li.Line.Height / 2);
                int x = (int)(rtl ? edge - 2 : edge + 2);
                Assert.False(IsYellow(shot.Pixel(x, y)), "line " + i + ": the space past the text (x " + x + ") is painted " + shot.Pixel(x, y));
                int yellow = 0;
                for (int px = 0; px < shot.Width; px++) if (IsYellow(shot.Pixel(px, (int)li.TextY + 1))) yellow++;
                Assert.True(yellow > 100, "line " + i + ": the text itself keeps its background (" + yellow + " yellow pixels in its top row)");
            }
        });
    }
}
