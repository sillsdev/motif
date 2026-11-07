using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Where a bullet or number goes when the text beside it is short of room: as in FieldWorks it takes a line of its own only when it
    /// is itself wider than the line, however much room the first word of the text needs.</summary>
    public class ListLabelLayoutTests
    {
        const string English = "Wonderful words follow the label and wrap onto more lines than one.";
        const string Ideographs = "这是一个很长的段落没有任何空格所以整个段落都算一个词语并且需要换行显示出来";
        const string ShortIdeographs = "这是一个很短的段落文字没有空";   // one "word" of 14 ideographs, about 220 pixels
        const string Thai = "ข้อความภาษาไทยที่ยาวมากไม่มีช่องว่างระหว่างคำดังนั้นทั้งย่อหน้าจึงเป็นคำเดียว";

        static ParagraphLayout Layout(string text, double width, string ws = "en", string before = "")
        {
            var p = Para(ws, ws, text);
            p.Props.SetInt(TextPropertyNumbers.LeadingIndent, 36000, TextPropertyNumbers.VariationMilliPoint);
            p.Props.SetInt(TextPropertyNumbers.FirstIndent, -24000, TextPropertyNumbers.VariationMilliPoint);
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);
            if (before.Length > 0) p.Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, before);
            p.Props.SetStr(TextPropertyNumbers.BulNumTxtAfter, ".");
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        [Theory]
        [InlineData(English, "en")]
        [InlineData(Ideographs, "zh")]
        [InlineData(ShortIdeographs, "zh")]
        [InlineData(Thai, "th")]
        public void A_label_that_fits_the_line_stays_beside_the_first_line_of_text_whatever_the_first_word_needs(string text, string ws) => Headless.Run(() =>
        {
            // A long label ("Chapter number 1.") takes most of a narrow line, but is narrower than it.
            foreach (double width in new[] { 200.0, 230, 260, 330 })
            {
                var pl = Layout(text, width, ws, "Chapter number ");
                Assert.True(pl.LabelRect.Width < pl.ContentWidth, "the label " + pl.LabelRect.Width + " fits the line " + pl.ContentWidth);
                Assert.Equal(pl.Lines[0].TextY, pl.LabelRect.Y, 2);
                Assert.True(pl.Lines[0].Line.Length > 0 && pl.Lines[0].X > pl.LabelRect.X, "the text starts beside the label");
            }
        });

        [Fact]
        public void A_label_wider_than_the_line_takes_a_line_of_its_own() => Headless.Run(() =>
        {
            var pl = Layout(English, 130, "en", "Chapter number ");
            Assert.True(pl.LabelRect.Width > pl.ContentWidth, "the label " + pl.LabelRect.Width + " is wider than the line " + pl.ContentWidth);
            Assert.True(pl.LabelRect.Bottom <= pl.Lines[0].TextY + 0.01, "the label is above the first line of text");
        });

        [Theory]
        [InlineData(-40000)]   // exact, taller than the label
        [InlineData(-8000)]    // exact, shorter than the label
        [InlineData(40000)]    // at least, taller than the label
        [InlineData(0)]        // no rule
        public void A_label_on_a_line_of_its_own_is_as_tall_as_the_label_whatever_the_line_height_rule(int lineHeight) => Headless.Run(() =>
        {
            var p = Para("en", "en", English);
            p.Props.SetInt(TextPropertyNumbers.LeadingIndent, 36000, TextPropertyNumbers.VariationMilliPoint);
            p.Props.SetInt(TextPropertyNumbers.FirstIndent, -24000, TextPropertyNumbers.VariationMilliPoint);
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);
            p.Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, "Chapter number ");
            p.Props.SetStr(TextPropertyNumbers.BulNumTxtAfter, ".");
            if (lineHeight != 0) p.Props.SetInt(TextPropertyNumbers.LineHeight, lineHeight, TextPropertyNumbers.VariationMilliPoint);
            var pl = Fixtures.Field(Doc(p), 130).ParagraphLayoutFor(0);
            var label = pl.LabelRect;
            Assert.True(label.Width > pl.ContentWidth, "the label takes a line of its own");
            Assert.Equal(pl.TopInset, label.Y, 2);
            Assert.Equal(label.Height, pl.Lines[0].Y - pl.TopInset, 2);
        });    }
}
