using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>NFD, surrogate pairs and private-use text survive editing; offsets are UTF-16 indices.</summary>
    public class TextFidelityTests
    {
        const string Nfd = "café naïve";
        const string Surrogates = "x\U0001F600y\U00020000z";
        const string PrivateUse = "pq\U000F0001r";

        [Fact]
        public void Text_is_unchanged_by_edits_elsewhere()
        {
            var text = Nfd + " | " + Surrogates + " | " + PrivateUse;
            var e = new DocumentEditor(Doc(Para("en", "en", text), Para("en", "en", "other para")));
            e.SetCaret(new TextPosition(1, 0));
            e.TypeText("ZZ");
            e.SetCaret(new TextPosition(1, 5));
            e.Backspace();
            Assert.Equal(text, e.Document.Paragraphs[0].Text);
            Assert.Equal(Interchange.Utf16Hex(text), Interchange.Utf16Hex(e.Document.Paragraphs[0].Text));
            e.SetCaret(new TextPosition(0, text.Length));
            e.TypeText("!");
            Assert.Equal(text + "!", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Backspace_removes_the_combining_mark_first_then_the_base()
        {
            var e = new DocumentEditor(OneParagraph("café"));
            e.SetCaret(new TextPosition(0, 5));
            e.Backspace();
            Assert.Equal("cafe", e.Document.Paragraphs[0].Text);
            e.Backspace();
            Assert.Equal("caf", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Delete_removes_the_whole_cluster()
        {
            var e = new DocumentEditor(OneParagraph("café"));
            e.SetCaret(new TextPosition(0, 3));
            e.DeleteForward();
            Assert.Equal("caf", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Backspace_removes_a_whole_surrogate_pair()
        {
            var e = new DocumentEditor(OneParagraph("a\U0001F600b"));
            e.SetCaret(new TextPosition(0, 3));
            e.Backspace();
            Assert.Equal("ab", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Offsets_are_utf16_and_an_object_counts_one()
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "a\U0001F600" });
            p.Runs.Add(Run.Object("fr", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "fr", Text = "éz" });
            var spans = p.Spans();
            Assert.Equal(new[] { 0, 3, 4 }, spans.Select(s => s.Start).ToArray());
            Assert.Equal(new[] { 3, 1, 3 }, spans.Select(s => s.Length).ToArray());
            Assert.Equal(new[] { "en", "fr", "fr" }, spans.Select(s => s.Ws).ToArray());
            Assert.Equal(7, p.Length);
        }
    }
}
