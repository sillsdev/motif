using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>An empty paragraph is as tall as a one-line paragraph of its style and writing system, and its caret as tall as the caret
    /// on that line.</summary>
    public class EmptyParagraphHeightTests
    {
        static ParagraphLayout Layout(string text, int? lineHeight, bool rtl)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            if (text.Length == 0) p.Runs.Clear();
            if (lineHeight != null) p.Props.SetInt(TextPropertyNumbers.LineHeight, lineHeight.Value, 1);
            return Fixtures.Field(Doc(p), 400).ParagraphLayoutFor(0);
        }

        public static readonly object[][] Cases =
        {
            new object[] { null!, false },
            new object[] { 30000, false },     // at least 30 pt
            new object[] { 10000, false },     // at least 10 pt: the text is taller
            new object[] { -30000, false },    // exactly 30 pt
            new object[] { -10000, false },    // exactly 10 pt
            new object[] { null!, true },
            new object[] { -30000, true },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void An_empty_paragraph_is_as_tall_as_a_paragraph_of_one_line_in_the_same_style(int? lineHeight, bool rtl) => Headless.Run(() =>
        {
            var full = Layout(rtl ? "ع" : "x", lineHeight, rtl);
            var empty = Layout(string.Empty, lineHeight, rtl);
            Assert.Single(full.Lines);
            Assert.Equal(full.Height, empty.Height, 2);
            var a = empty.CaretRect(0);
            var b = full.CaretRect(0);
            Assert.Equal(b.Height, a.Height, 2);
            Assert.Equal(b.Y, a.Y, 2);
        });
    }
}
