using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A justified right-to-left paragraph of several runs: its lines are stretched from the runs as drawn.</summary>
    public class JustifiedRtlRunsTests
    {
        public static IEnumerable<object[]> CrashCases()
        {
            foreach (var width in new[] { 80.0, 140, 300 })
                foreach (var split in new[] { 1, 4, 7 })
                    yield return new object[] { width, split };
        }

        [Theory]
        [MemberData(nameof(CrashCases))]
        public void A_justified_right_to_left_paragraph_of_two_runs_lays_out_selects_and_draws_whatever_the_width(double width, int split) => Headless.Run(() =>
        {
            // On main the layout threw "Covered length must be greater than zero" for such a paragraph.
            var p = TwoRuns(split);
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var field = Fixtures.Field(Doc(p), width);
            var pl = field.ParagraphLayoutFor(0);
            Assert.NotEmpty(pl.SelectionRects(0, p.Length));
            Fixtures.Render(field);
        });

        static Paragraph TwoRuns(int split)
        {
            const string text = "fox fox fox   fox fox";
            var p = Para("ar", "en", text.Substring(0, split), "en", text.Substring(split));
            p.Rtl = true;
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            return p;
        }
    }
}
