using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0021: the information a context-menu request reports.</summary>
    public class ContextInfoTests
    {
        static DocumentEditor Mixed()
        {
            var bold = new Run { Ws = "fr", Text = "bonjour" };
            bold.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "Hello " });
            p.Runs.Add(bold);
            p.Runs.Add(new Run { Ws = "en", Text = " world" });
            return new DocumentEditor(Doc(p));
        }

        [Fact]
        public void A_point_on_a_word_reports_the_word_its_range_its_writing_system_and_its_run()
        {
            var e = Mixed();
            var info = e.DescribeContext(new TextPosition(0, 8), false);
            Assert.Equal("bonjour", info.Word);
            Assert.Equal(new TextRange(0, 6, 13), info.WordRange);
            Assert.Equal("fr", info.WritingSystem);
            Assert.Equal("bonjour", info.Run!.Text);
            Assert.Equal(new TextRange(0, 6, 13), info.RunRange);
            Assert.True(info.Run.Props.TryGetInt(TextPropertyNumbers.Bold, out _));
            Assert.Null(info.EmbeddedObject);
            Assert.Empty(info.SpellingSuggestions);
        }

        [Fact]
        public void A_point_on_white_space_has_no_word_but_still_reports_the_run_and_writing_system()
        {
            var info = Mixed().DescribeContext(new TextPosition(0, 5), false);
            Assert.Equal(string.Empty, info.Word);
            Assert.True(info.WordRange.IsEmpty);
            Assert.Equal("en", info.WritingSystem);
            Assert.Equal(new TextRange(0, 0, 6), info.RunRange);
        }

        [Fact]
        public void The_run_is_a_copy_the_host_cannot_use_to_change_the_document()
        {
            var e = Mixed();
            var info = e.DescribeContext(new TextPosition(0, 8), false);
            info.Run!.Text = "changed";
            info.Run.Props.SetInt(TextPropertyNumbers.Italic, 1);
            Assert.Equal("bonjour", e.Document.Paragraphs[0].Runs[1].Text);
            Assert.False(e.Document.Paragraphs[0].Runs[1].Props.TryGetInt(TextPropertyNumbers.Italic, out _));
        }

        [Fact]
        public void A_keyboard_request_at_the_end_of_a_word_means_that_word_and_at_a_space_between_words_none()
        {
            var e = Mixed();
            Assert.Equal("Hello", e.DescribeContext(new TextPosition(0, 5), true).Word);
            Assert.Equal("bonjour", e.DescribeContext(new TextPosition(0, 13), true).Word);
            Assert.Equal("Hello", e.DescribeContext(new TextPosition(0, 0), true).Word);
            var between = Doc(Para("en", "en", "a  b"));
            Assert.Equal(string.Empty, new DocumentEditor(between).DescribeContext(new TextPosition(0, 2), true).Word);
        }

        [Fact]
        public void An_empty_paragraph_reports_its_default_writing_system_and_no_run()
        {
            var p = new Paragraph { DefaultWs = "ar" };
            var info = new DocumentEditor(Doc(p)).DescribeContext(new TextPosition(0, 0), true);
            Assert.Equal("ar", info.WritingSystem);
            Assert.Null(info.Run);
            Assert.Equal(string.Empty, info.Word);
        }

        [Fact]
        public void An_embedded_object_is_reported_with_its_data_and_whether_it_is_a_link()
        {
            var link = ((char)4) + "http://example.org";
            var picture = ((char)8) + "pic-red";
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "see " });
            p.Runs.Add(Run.Object("en", link));
            p.Runs.Add(new Run { Ws = "en", Text = " and " });
            p.Runs.Add(Run.Object("en", picture));
            var e = new DocumentEditor(Doc(p));
            var onLink = e.DescribeContext(new TextPosition(0, 4), false);
            Assert.Equal(link, onLink.EmbeddedObject!.Data);
            Assert.True(onLink.EmbeddedObject.IsLink);
            Assert.Equal(new TextRange(0, 4, 5), onLink.EmbeddedObject.Range);
            Assert.Equal(string.Empty, onLink.Word);
            var onPicture = e.DescribeContext(new TextPosition(0, 10), false);
            Assert.False(onPicture.EmbeddedObject!.IsLink);
            Assert.Equal(picture, onPicture.EmbeddedObject.Data);
            Assert.Equal(link, e.DescribeContext(new TextPosition(0, 5), true).EmbeddedObject!.Data);
        }

        [Fact]
        public void The_selection_and_its_text_are_reported()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one two"), Para("en", "en", "three")));
            Assert.False(e.DescribeContext(new TextPosition(0, 1), false).HasSelection);
            e.SetCaret(new TextPosition(0, 4));
            e.SetCaret(new TextPosition(1, 3), true);
            var info = e.DescribeContext(new TextPosition(0, 5), false);
            Assert.True(info.HasSelection);
            Assert.Equal(new TextRange(new TextPosition(0, 4), new TextPosition(1, 3)), info.Selection);
            Assert.Equal("two\nthr", info.SelectedText);
            Assert.Equal("two", info.Word);
        }
    }
}
