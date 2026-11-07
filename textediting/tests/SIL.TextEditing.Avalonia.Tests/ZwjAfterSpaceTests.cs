using System.Text;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A zero width joiner after a space before Arabic must not make the layout throw (issue 112).</summary>
    public class ZwjAfterSpaceTests
    {
        const string Unit = "هذه ‍ فقرة عربية طويلة ";

        static string Repeated(int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(Unit);
            return sb.ToString();
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 5)]
        [InlineData(true, 0)]
        public void A_zero_width_joiner_after_a_space_before_Arabic_lays_out_at_every_width_though_Avalonia_11_3_throws_Cannot_split(bool rtl, int align) => Headless.Run(() =>
        {
            int laid = 0;
            for (double width = 30; width <= 420; width += 1)
            {
                var p = Para("en");
                for (int i = 0; i < 8; i++)
                {
                    p.Runs.Add(new Run { Ws = "en", Text = "هذه ‍" });
                    p.Runs.Add(new Run { Ws = "ar", Text = " فقرة عربية طويلة " });
                }
                p.Rtl = rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                var field = Fixtures.Field(Doc(p), width);
                var pl = field.ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
                Assert.Equal(p.Length, pl.Lines[pl.Lines.Count - 1].End);
                laid++;
            }
            Assert.Equal(391, laid);
        });

        [Fact]
        public void Two_runs_with_the_joiner_at_the_end_of_the_first_lay_out_and_keep_their_caret_stops() => Headless.Run(() =>
        {
            var p = Para("en", "en", " ‍", "fr", "جمل");
            for (double width = 30; width <= 200; width += 5)
            {
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
                for (int i = 0; i <= p.Length; i++) pl.CaretRect(i);
            }
        });
    }
}
