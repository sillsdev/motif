using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0030: single-line mode is single-paragraph mode without the hard line break.</summary>
    public class SingleLineTests
    {
        [Fact]
        public void Single_line_mode_is_single_paragraph_and_has_no_hard_line_break()
        {
            var e = new DocumentEditor(TestDocs.OneParagraph("ab")) { SingleLine = true };
            Assert.True(e.SingleParagraph);
            e.SetCaret(new TextPosition(0, 2));
            e.InsertLineBreak();
            Assert.Equal("ab", e.Document.Paragraphs[0].Text);
            e.TypeText("x\u2028y\nz");
            Assert.Equal("abx y z", e.Document.Paragraphs[0].Text);
            e.PasteText("p\u2028q");
            Assert.Equal("abx y zp q", e.Document.Paragraphs[0].Text);
            var para = TestDocs.Para("en", "en", "m\u2028n");
            e.ReplaceRange(new TextPosition(0, 0), new TextPosition(0, 1), new System.Collections.Generic.List<Paragraph> { para }, "paste");
            Assert.Equal("m nbx y zp q", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Single_paragraph_mode_alone_still_takes_a_hard_line_break()
        {
            var e = new DocumentEditor(TestDocs.OneParagraph("ab")) { SingleParagraph = true };
            e.SetCaret(new TextPosition(0, 2));
            e.InsertLineBreak();
            Assert.Equal("ab\u2028", e.Document.Paragraphs[0].Text);
        }
    }
}
