using System;
using System.Collections.Generic;
using SIL.TextEditing.Markdown.Writer;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    public class InvisibleTextTests
    {
        [Theory]
        [InlineData("\u200D", 0, false)]
        [InlineData("\u200D\U0001F642", 0, false)]
        [InlineData("\U0001F642\u200D", 2, false)]
        [InlineData("\uD800\u200D\U0001F642", 1, false)]
        [InlineData("\uDC00\u200D\U0001F642", 1, false)]
        [InlineData("\U0001F642\u200D\uD800", 2, false)]
        [InlineData("\U0001F642\u200D\uDC00", 2, false)]
        [InlineData("\U0001F469\U0001F3FD\u200D\U0001F4BB", 4, true)]
        [InlineData("\u2764\uFE0F\u200D\U0001F525", 2, true)]
        [InlineData("\U0001F642\u200D\u2194\uFE0F", 2, true)]
        [InlineData("\u2300\u200D\u2300", 1, false)]
        [InlineData("a\u200D\U0001F642", 1, false)]
        [InlineData("\U0001F642\u200Da", 2, false)]
        public void JoinsEmojiChecksItsNeighbours(string text, int at, bool expected)
        {
            Assert.Equal(expected, Chars.JoinsEmoji(text, at));
        }

        [Theory]
        [InlineData("\U0001F642\u200D\u2194\uFE0F", "\U0001F642\u200D\u2194\uFE0F")]
        [InlineData("\u2300\u200D\u2300", "\u2300&#x200D;\u2300")]
        public void EmojiJoinersUseTheUnicodeProperty(string text, string expected)
        {
            Check(B.Plain(text), expected + "\n");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnEmojiJoinerAtARunBoundaryIsVisibleAndRoundTrips(bool joinerInFirstRun)
        {
            string first = "\U0001F469" + (joinerInFirstRun ? "\u200D" : "");
            string second = (joinerInFirstRun ? "" : "\u200D") + "\U0001F4BB";
            var paragraph = B.P("Normal", B.En(first), B.BoldRun("en", second));
            string markdown = MarkdownConverter.ToMarkdown(B.Doc(paragraph), Hosts.Project());
            Assert.Contains("&#x200D;", markdown);
            Assert.DoesNotContain("\u200D", markdown, StringComparison.Ordinal);
            RoundTrip(paragraph, markdown);
        }

        [Theory]
        [InlineData("Normal")]
        [InlineData("Heading 1")]
        [InlineData("GDR Table Header")]
        public void InvisiblesAndNbspAreReferencesBesideEmphasisPunctuation(string style)
        {
            const string text = "*\u200B_\u200D*\u00AD_\u200E*\u200F_\u00A0*";
            const string escaped = "\\*&#x200B;\\_&#x200D;\\*&#xAD;\\_&#x200E;\\*&#x200F;\\_&nbsp;\\*";
            string expected = style == "Heading 1" ? "# " + escaped + "\n"
                : style == "GDR Table Header" ? "| " + escaped + " |\n|---|\n" : escaped + "\n";
            Check(B.P(style, B.En(text)), expected);
        }

        [Fact]
        public void NumericModeWritesEvenEmojiJoinersAndNbspAsNumericReferences()
        {
            const string text = "\u200B\u200D\u00AD\u200E\u200F\u00A0* _\U0001F469\u200D\U0001F4BB";
            var paragraph = B.Plain(text);
            var generator = new InlineGenerator(Hosts.Project(), BlockContext.Plain());
            string markdown = generator.Generate(new[] { new List<Run>(paragraph.Runs) }, new GenMode { Numeric = true });
            Assert.Equal("&#x200B;&#x200D;&#xAD;&#x200E;&#x200F;&#xA0;&#x2A;&#x20;&#x5F;&#x1F469;&#x200D;&#x1F4BB;", markdown);
            RoundTrip(paragraph, markdown + "\n");
        }

        static void Check(Paragraph paragraph, string expected)
        {
            string markdown = MarkdownConverter.ToMarkdown(B.Doc(paragraph), Hosts.Project());
            Assert.Equal(expected, markdown);
            RoundTrip(paragraph, markdown);
        }

        static void RoundTrip(Paragraph paragraph, string markdown)
        {
            var back = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.False(back.HasErrors);
            Assert.True(MarkdownConverter.AreEquivalent(new[] { paragraph }, back.Document.Paragraphs));
        }
    }
}
