using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A right-to-left mark before the space a line wraps at must not make the layout throw (issue 106).</summary>
    public class RlmWrapTests
    {
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 3)]
        [InlineData(true, 0)]
        [InlineData(true, 4)]
        public void A_right_to_left_mark_before_a_space_at_a_wrapped_line_end_lays_out_though_Avalonia_12_refuses_a_range_of_no_width(bool rtl, int align) => Headless.Run(() =>
        {
            foreach (var text in new[] { "alpha‏ bravo‏ charlie‏ delta‏ echo‏ foxtrot‏ golf‏ hotel‏ india‏ juliet‏ kilo", "alpha ‏ bravo ‏ charlie ‏ delta ‏ echo ‏ foxtrot ‏ golf ‏ hotel", "‏ ‏ ‏ ‏ alpha ‏ ‏ bravo ‏ ‏ charlie" })
            {
            int laid = 0;
            for (double width = 40; width <= 200; width += 1)
            {
                var p = Para("en", "en", text);
                p.Rtl = rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                p.Props.SetInt(TextPropertyNumbers.MaxLines, 2);
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
                laid++;
            }
            Assert.True(laid > 100);
            }
        });

        [Fact]
        public void A_justified_line_with_an_Arabic_letter_mark_and_spaces_round_an_isolate_lays_out_though_Avalonia_12_refuses_a_range_of_no_width_in_Justify() => Headless.Run(() =>
        {
            string text = "-  i" + char.ConvertFromUtf32(0x1F600) + "؜  ⁩ ع";
            for (double width = 20; width <= 120; width += 1)
            {
                var p = Para("ja", "ja", text);
                p.Props.SetInt(TextPropertyNumbers.Align, 5);
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
            }
        });
    }
}
