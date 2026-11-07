using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Under an exact line height the baseline sits at one proportion of every slot. As in FieldWorks that proportion is the share of the
    /// ascent in the height of the text that most characters of the paragraph's first line are in, cut to a whole per cent.</summary>
    public class ExactLineHeightTests
    {
        const string Long = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";

        static Paragraph Mixed(string headline, string headlineFont, double headlineSize)
        {
            var p = Para("en", "en", headline, "en", Long);
            p.Runs[0].Props.SetStr(TextPropertyNumbers.FontFamily, headlineFont).SetInt(TextPropertyNumbers.FontSize, (int)(headlineSize * 1000), 1);
            p.Runs[1].Props.SetStr(TextPropertyNumbers.FontFamily, "Segoe UI").SetInt(TextPropertyNumbers.FontSize, 12000, 1);
            p.Props.SetInt(TextPropertyNumbers.LineHeight, -30000, 1);
            return p;
        }

        static ParagraphLayout Layout(Paragraph p, double width = 400) => Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);

        static double BaselineIn(LineInfo li) => li.TextY + li.Line.Baseline - li.Y;

        [Fact]
        public void The_baseline_proportion_comes_from_the_font_most_characters_of_the_first_line_are_in_not_from_the_tallest_run() => Headless.Run(() =>
        {
            // The first line is a short run of a 20 point Constantia heading and the rest in 12 point Segoe UI: the line as a whole
            // (tallest ascent over the deepest descent) is in Constantia's proportion, the characters are mostly in Segoe UI's.
            var plain = Layout(Mixed("", "Segoe UI", 12));
            double segoe = System.Math.Floor(plain.Lines[0].Line.Baseline / plain.Lines[0].Line.Height * 100) / 100;
            var pl = Layout(Mixed("Title ", "Constantia", 20));
            Assert.True(pl.Lines.Count > 2);
            double wholeLine = pl.Lines[0].Line.Baseline / pl.Lines[0].Line.Height;
            Assert.True(System.Math.Abs(wholeLine - segoe) > 0.05, "the test needs fonts that differ: " + wholeLine + " and " + segoe);
            foreach (var li in pl.Lines) Assert.Equal(segoe * 30, BaselineIn(li), 1);
        });

        [Fact]
        public void The_proportion_is_the_first_lines_and_is_used_for_every_line_even_when_later_lines_are_in_another_font() => Headless.Run(() =>
        {
            var p = Para("en", "en", "Constantia words come first in this paragraph and fill the first line ", "en", Long);
            p.Runs[0].Props.SetStr(TextPropertyNumbers.FontFamily, "Constantia").SetInt(TextPropertyNumbers.FontSize, 12000, 1);
            p.Runs[1].Props.SetStr(TextPropertyNumbers.FontFamily, "Segoe UI").SetInt(TextPropertyNumbers.FontSize, 12000, 1);
            p.Props.SetInt(TextPropertyNumbers.LineHeight, -30000, 1);
            var pl = Layout(p, 300);
            double first = System.Math.Floor(pl.Lines[0].Line.Baseline / pl.Lines[0].Line.Height * 100) / 100;
            Assert.True(pl.Lines.Count > 3);
            foreach (var li in pl.Lines) Assert.Equal(first * 30, BaselineIn(li), 1);
        });

        [Fact]
        public void The_proportion_is_cut_to_a_whole_per_cent() => Headless.Run(() =>
        {
            var p = Para("en", "en", Long);
            p.Runs[0].Props.SetStr(TextPropertyNumbers.FontFamily, "Constantia");
            p.Props.SetInt(TextPropertyNumbers.LineHeight, -30000, 1);
            var pl = Layout(p);
            double exact = pl.Lines[0].Line.Baseline / pl.Lines[0].Line.Height;   // 0.7056 for this face
            Assert.NotEqual(System.Math.Floor(exact * 100) / 100, exact, 3);
            foreach (var li in pl.Lines) Assert.Equal(System.Math.Floor(exact * 100) / 100 * 30, BaselineIn(li), 2);
        });
    }
}
