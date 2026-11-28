using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Issue 488: a host that names a code style reads a code span as a run of that style and writes such a run back as a code span; a host that
    /// names none still refuses a code span.</summary>
    public class CodeSpanTests
    {
        const string Style = "Code";

        static MarkdownOptions WithCode()
        {
            var options = Hosts.Project();
            options.StyleNames.Code = Style;
            return options;
        }

        static string? StyleOf(Run run) => run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out string style) ? style : null;

        static string RoundTrip(string text, out Run[] runs)
        {
            var options = WithCode();
            var read = MarkdownConverter.Parse(text, options);
            Assert.DoesNotContain(read.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            runs = read.Document.Paragraphs.SelectMany(p => p.Runs).ToArray();
            string written = MarkdownConverter.ToMarkdown(read.Document, options);
            var back = MarkdownConverter.Parse(written, options);
            Assert.DoesNotContain(back.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.True(MarkdownConverter.AreEquivalent(read.Document.Paragraphs, back.Document.Paragraphs), written);
            return written;
        }

        [Fact]
        public void ACodeSpanIsARunWithTheCodeStyleAndPlainTextAroundItIsNot()
        {
            string written = RoundTrip("See `AaIUe36vzwMPYN1owvSyWg` and `motif guide add` now.", out var runs);

            Assert.Equal(new[] { "See ", "AaIUe36vzwMPYN1owvSyWg", " and ", "motif guide add", " now." }, runs.Select(r => r.Text));
            Assert.Equal(new[] { null, Style, null, Style, null }, runs.Select(StyleOf));
            Assert.Contains("`AaIUe36vzwMPYN1owvSyWg`", written);
            Assert.Contains("`motif guide add`", written);
            Assert.DoesNotContain("custom-style", written);
        }

        [Fact]
        public void ARegexKeepsUnderscoreStarAndBracketLiteralWithNoEscaping()
        {
            string written = RoundTrip("Match `^(a_b|c*d)[kK]\\d+$` here.", out var runs);

            Assert.Equal("^(a_b|c*d)[kK]\\d+$", runs[1].Text);
            Assert.Contains("`^(a_b|c*d)[kK]\\d+$`", written);
        }

        [Fact]
        public void ABacktickInsideTheSpanLengthensTheFence()
        {
            string written = RoundTrip("Use ``a`b`` here.", out var runs);

            Assert.Equal("a`b", runs[1].Text);
            Assert.Contains("``a`b``", written);
        }

        [Fact]
        public void AnEscapedBacktickIsALiteralBacktickInPlainText()
        {
            string written = RoundTrip("A \\`literal\\` tick and `code`.", out var runs);

            Assert.Equal("A `literal` tick and ", runs[0].Text);
            Assert.Null(StyleOf(runs[0]));
            Assert.Contains("\\`literal\\`", written);
        }

        [Fact]
        public void ARunWhoseTextCannotStandInBackticksIsWrittenAsASpanAndReadsBackTheSame()
        {
            var options = WithCode();
            foreach (string text in new[] { " lead", "trail ", "`edge", "two  spaces", "tab\there", "a|b" })
            {
                var document = B.Doc(B.P("Normal", B.En("x "), B.Styled("en", text, Style), B.En(" y")));
                string written = MarkdownConverter.ToMarkdown(document, options);
                var back = MarkdownConverter.Parse(written, options);
                Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs), text + " -> " + written);
            }
        }

        [Fact]
        public void AHostWithNoCodeStyleStillRefusesACodeSpan()
        {
            var read = MarkdownConverter.Parse("A `code` span.", Hosts.Project());

            Assert.Contains(read.Diagnostics, d => d.Id == MarkdownDiagnosticIds.UnsupportedMarkup && d.Severity == MarkdownSeverity.Error);
        }

        [Fact]
        public void ACodeSpanInsideEmphasisKeepsTheEmphasisAndTheStyleAndWritesAsASpan()
        {
            string written = RoundTrip("Say *`motif`* now.", out var runs);

            Assert.Equal(Style, StyleOf(runs[1]));
            Assert.NotNull(written);
        }
    }
}
