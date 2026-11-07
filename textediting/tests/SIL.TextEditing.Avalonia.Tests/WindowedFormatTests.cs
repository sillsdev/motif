using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A paragraph of very many runs, with nothing in it that runs right to left, has its lines formatted from a window of its text and not from all that is
    /// left of it, which is much of what a layout costs (issue 111); its lines, their widths and the places of its carets are those the formatter gives when it is
    /// given the whole text. A paragraph with right-to-left text is formatted whole, as the directions of its text are resolved for all of it at once.</summary>
    public class WindowedFormatTests
    {
        static readonly string[] Words = { "alpha", "fox", "Wonderful", "ill", "12", "ét", "jumps", "x", "i", "dog.", "e" + (char)0x301, "jig", "WAVE", "fi" };
        static readonly string[] Arabic = { "هذه", "فقرة", "عربية" };
        static readonly string[] Spaces = { " ", "   ", ((char)0xA0).ToString(), " ", " " };

        static Paragraph Make(Random rng, int runs, int align, int maxLines, bool withArabic = false)
        {
            var p = Para("en");
            for (int i = 0; i < runs; i++)
            {
                var sb = new StringBuilder();
                for (int t = rng.Next(1, 4); t > 0; t--)
                {
                    sb.Append(withArabic && rng.Next(8) == 0 ? Arabic[rng.Next(Arabic.Length)] : Words[rng.Next(Words.Length)]);
                    sb.Append(Spaces[rng.Next(Spaces.Length)]);
                    if (rng.Next(60) == 0) sb.Append((char)0x2028);
                }
                var r = new Run { Ws = rng.Next(3) == 0 ? "fr" : "en", Text = sb.ToString() };
                if (rng.Next(2) == 0) r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                if (rng.Next(7) == 0) r.Props.SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn);
                if (rng.Next(9) == 0) r.Props.SetInt(TextPropertyNumbers.Offset, 2000, TextPropertyNumbers.VariationMilliPoint);
                p.Runs.Add(r);
            }
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            if (maxLines > 0) p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines);
            return p;
        }

        static List<string> Shape(Paragraph paragraph, double width, int windowedFrom, out long runsRead)
        {
            int was = ParagraphLayout.WindowedFormatSegments;
            ParagraphLayout.WindowedFormatSegments = windowedFrom;
            try
            {
                using (var probe = LayoutProbe.Start())
                {
                    var pl = Fixtures.Field(Doc(paragraph.Clone()), width).ParagraphLayoutFor(0);
                    runsRead = probe.RunsRead;
                    var result = pl.Lines.Select(l => l.Start + ".." + l.End + " w " + l.Line.Width.ToString("F3") + " ws " + l.Line.WidthIncludingTrailingWhitespace.ToString("F3") + " h " + l.Height.ToString("F3") + " x " + l.X.ToString("F3")
                        + " stops " + string.Join(",", pl.VisualStops(l).Select(st => st.Offset + "@" + st.X.ToString("F2")))).ToList();
                    result.Add("height " + pl.Height.ToString("F3") + " truncated " + pl.Truncated);
                    return result;
                }
            }
            finally { ParagraphLayout.WindowedFormatSegments = was; }
        }

        [Theory]
        [InlineData(1, 0, 0)]
        [InlineData(2, 5, 0)]
        [InlineData(3, 3, 0)]
        [InlineData(4, 2, 0)]
        [InlineData(5, 0, 4)]
        [InlineData(6, 5, 3)]
        public void The_lines_from_a_window_of_the_text_are_the_lines_from_the_whole_of_it(int seed, int align, int maxLines) => Headless.Run(() =>
        {
            var rng = new Random(seed);
            for (int c = 0; c < 6; c++)
            {
                var paragraph = Make(rng, rng.Next(120, 400), align, maxLines);
                double width = rng.Next(80, 700);
                var whole = Shape(paragraph, width, int.MaxValue, out long wholeReads);
                var windowed = Shape(paragraph, width, 8, out long windowReads);
                Assert.True(whole.Count > 4 || maxLines > 0, "lines: " + whole.Count);
                Assert.True(windowReads != wholeReads, "reads of the windowed layout: " + windowReads + ", of the whole: " + wholeReads);   // the window was used: it reads some runs again for each line, or stops at the line limit without reading the rest
                Assert.Equal(whole, windowed);
            }
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_paragraph_with_right_to_left_text_in_it_or_in_that_direction_is_formatted_whole(bool rtl) => Headless.Run(() =>
        {
            var rng = new Random(8);
            var paragraph = Make(rng, 300, 0, 0, withArabic: !rtl);
            paragraph.Rtl = rtl;
            Shape(paragraph, 300, 8, out long reads);
            Assert.True(reads <= 300 + 2, "runs read: " + reads + " for a paragraph of 300 runs at most");   // each run once: the whole text is given to the formatter
        });

        [Fact]
        public void A_paragraph_of_a_few_hundred_runs_is_not_given_to_the_formatter_in_windows() => Headless.Run(() =>
        {
            Assert.True(ParagraphLayout.WindowedFormatSegments >= 10000);
            var p = Make(new Random(1), 600, 0, 0);
            Shape(p, 400, ParagraphLayout.WindowedFormatSegments, out long reads);
            Assert.True(reads <= 600 + 2, "runs read: " + reads);
        });

        [Theory]
        [InlineData(0x4E2D)]   // a CJK ideograph
        [InlineData(0x0E01)]   // a Thai consonant
        [InlineData(0x3042)]   // hiragana
        public void A_paragraph_with_text_of_a_script_that_falls_back_to_another_font_is_formatted_whole(int codePoint) => Headless.Run(() =>
        {
            var rng = new Random(5);
            var paragraph = Make(rng, 300, 0, 0);
            paragraph.Runs[150].Text += char.ConvertFromUtf32(codePoint) + (char)0x9 + "x ";   // fallback text, then a neutral character at what may be a line start
            Shape(paragraph, 200, 8, out long reads);
            Assert.True(reads <= 300 + 2, "runs read: " + reads + " for a paragraph of 300 runs at most");   // each run once: the whole text is given to the formatter
        });

        [Fact]
        public void Kerning_across_a_line_start_moves_a_width_by_a_fraction_of_a_pixel_and_the_lines_are_the_same_in_a_windowed_paragraph() => Headless.Run(() =>
        {
            var p = Para("en");
            for (int i = 0; i < 300; i++) p.Runs.Add(new Run { Ws = "en", Text = (i % 2 == 0 ? "WAVE AV VE " : "To Ty Va ") });
            for (int i = 0; i < p.Runs.Count; i += 2) p.Runs[i].Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            int was = ParagraphLayout.WindowedFormatSegments;
            try
            {
                for (double width = 60; width <= 300; width += 13)
                {
                    ParagraphLayout.WindowedFormatSegments = int.MaxValue;
                    var whole = Fixtures.Field(Doc(p.Clone()), width).ParagraphLayoutFor(0).Lines;
                    var wholeLines = whole.Select(l => (l.Start, l.End, l.Line.WidthIncludingTrailingWhitespace)).ToList();
                    ParagraphLayout.WindowedFormatSegments = 8;
                    var windowed = Fixtures.Field(Doc(p.Clone()), width).ParagraphLayoutFor(0).Lines;
                    Assert.Equal(wholeLines.Count, windowed.Count);
                    for (int i = 0; i < windowed.Count; i++)
                    {
                        Assert.Equal((wholeLines[i].Start, wholeLines[i].End), (windowed[i].Start, windowed[i].End));
                        Assert.InRange(windowed[i].Line.WidthIncludingTrailingWhitespace - wholeLines[i].Item3, -1.0, 1.0);   // kerning, a fraction of a pixel
                    }
                }
            }
            finally { ParagraphLayout.WindowedFormatSegments = was; }
        });
    }
}
