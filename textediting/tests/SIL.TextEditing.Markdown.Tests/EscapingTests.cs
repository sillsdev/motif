using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Spec 3.4: the writer escapes by the rules of the escape table, however long the paragraph, so ordinary prose needs no escape at all. The rules and the
    /// canonical spelling of each case are conformance fixtures (tests/conformance/03-escapes.json); these tests are about size and about text the rules must carry
    /// through the read-back unchanged.</summary>
    public class EscapingTests
    {
        static string Write(params Paragraph[] paragraphs) => MarkdownConverter.ToMarkdown(B.Doc(paragraphs), Hosts.Project());

        static int Backslashes(string text) => text.Count(c => c == '\\');

        // Ordinary prose punctuation: none of it changes the reading.
        const string Prose = "ab. cd-e (f) g: h= ";

        [Theory]
        [InlineData(50)]
        [InlineData(1200)]
        public void ProseNeedsNoEscapeHoweverLongTheParagraph(int repeats)
        {
            string prose = string.Concat(Enumerable.Repeat(Prose, repeats));
            string text = Write(B.Plain(prose));
            Assert.Equal(0, Backslashes(text));
            Assert.Equal(prose.TrimEnd(' ') + "&#x20;\n", text);
        }

        [Fact]
        public void AStarIsAlwaysEscapedAndNothingElseInProseIs()
        {
            string text = Write(B.Plain("*word* " + string.Concat(Enumerable.Repeat(Prose, 300))));
            Assert.Equal(2, Backslashes(text));
            Assert.StartsWith("\\*word\\* ab. cd-e (f) g: h= ", text);
        }

        [Fact]
        public void AParagraphOfManyLookalikesStillReadsBack()
        {
            string text = string.Concat(Enumerable.Repeat("*w* _v_ [x]{.v} ", 300));
            var paragraph = B.Plain(text);
            string markdown = Write(paragraph);
            var back = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.False(back.HasErrors);
            Assert.True(MarkdownConverter.AreEquivalent(new[] { paragraph }, back.Document.Paragraphs));
        }

        [Fact]
        public void ALongTableCellNeedsOnlyTheEscapesTheRulesGive()
        {
            string cell = "*word* " + string.Concat(Enumerable.Repeat(Prose, 300));
            var header = B.P("GDR Table Header", B.En("a\t" + cell));
            string text = Write(header);
            Assert.Equal(2, Backslashes(text));
        }

        // A long run of spaces used to be written in quadratic time (each space asked whether only spaces followed). The size is what the
        // check is: before the change this took many seconds, now it is one pass.
        [Theory]
        [InlineData("a", 100_000, "b")]
        [InlineData("a", 100_000, "")]
        [InlineData("", 100_000, "")]
        public void ALongRunOfSpacesIsWrittenAsReferences(string before, int spaces, string after)
        {
            string text = before + new string(' ', spaces) + after;
            string markdown = Write(B.Plain(text));
            Assert.Equal(before + string.Concat(Enumerable.Repeat("&#x20;", spaces)) + after + "\n", markdown);
            var back = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.Equal(text, back.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void SpacesAtTheEndOfACellAreReferencesAndTheOnesBetweenWordsAreNot()
        {
            string markdown = Write(B.P("GDR Table Header", B.En("a b  \tc d   ")));
            Assert.Equal("| a b&#x20;&#x20; | c d&#x20;&#x20;&#x20; |\n|---|---|\n", markdown);
        }
    }
}
