using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Cases the random documents found: each one broke a writer that checked its paragraphs only one at a time.</summary>
    public class RegressionTests
    {
        static string RoundTrip(params Paragraph[] paragraphs)
        {
            var options = Hosts.Project(new TestObjects());
            var document = B.Doc(paragraphs);
            string markdown = MarkdownConverter.ToMarkdown(document, options);
            var parsed = MarkdownConverter.Parse(markdown, options);
            Assert.DoesNotContain(parsed.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, parsed.Document.Paragraphs), markdown + "\n" + ModelDump.Of(parsed.Document));
            Assert.Equal(markdown, MarkdownConverter.ToMarkdown(parsed.Document, options));
            return markdown;
        }

        [Fact]
        public void ListsSeparatedByAFenceStayTwoLists()
            => Assert.Equal("- x\n\n::: {custom-style=Example}\nf\n:::\n\n- y\n",
                RoundTrip(B.P("Bulleted List", B.En("x")), B.P("Example", B.En("f")), B.P("Bulleted List", B.En("y"))));

        [Fact]
        public void ALinkTextIsOneRunEvenWhenItHoldsAnEscapedBracket()
            => RoundTrip(B.P("Normal", B.R("en", "a]b[c", p => p.SetStr(133, "Hyperlink"), TestObjects.ExternalPath + "x")));

        [Fact]
        public void AnAttributeBlockAfterALinkIsFoundInATableThatDoesNotStartTheDocument()
            => RoundTrip(B.Plain("before"),
                B.P("GDR Table Header", B.R("en", "t", p => p.SetStr(133, "Hyperlink").SetInt(3, 1, 3), TestObjects.ExternalPath + "https://x.org")));

        [Fact]
        public void ABracketLeftOpenInOneCellDoesNotSwallowTheNextRow()
            => RoundTrip(B.P("GDR Table Header", B.En("a [2)\tb")), B.P("GDR Table Row", B.En("[x]{v} c\td")));

        [Fact]
        public void ATableRowThatEndsInABackslashDoesNotJoinTheNextOrphanRow()
            => RoundTrip(B.P("GDR Table Row", B.En("a\\\\\tb\\\\")), B.P("GDR Table Row", B.En("c\td")));

        [Fact]
        public void FenceLinesThatEndInBackslashesStayApart()
            => RoundTrip(B.P("Example", B.En("a \\\\\\\\")), B.P("Example", B.En("b")), B.P("Example", B.En("c\\")), B.P("Example", B.En("d")));

        [Fact]
        public void ALineThatLooksLikeAReferenceDefinitionOnlyWithALineEndIsEscaped()
            => RoundTrip(B.Plain("[x]:::mang  ng{}{a,b}!::: ~"), B.Plain("[x]:abc"));

        [Fact]
        public void AnEmptyParagraphWhoseStringPropertyLooksLikeALinkAndABlockRoundTrips()
            => RoundTrip(B.Empty("Heading 1", "ar").WithProps(p => p.SetStr(2, "[x](y){a,b} ").SetStr(147, "mang")));

        [Fact]
        public void ABackslashBeforeALineSeparatorIsEscaped()
            => RoundTrip(B.Plain("a\\\x2028b"), B.Plain("a\\\\\x2028b"));

        [Fact]
        public void SingleCharacterParagraphsRoundTripInEveryBlock()
        {
            foreach (var text in new[]
            {
                "[", "]", "{", "}", "\\", "|", "*", "_", "`", "<", "&", "#", "-", "+", ">", ":", "!", "~", "=", ".", "1", "1.", "-x", "\t", " ", "\x00A0", "\xFFFC",
                "\x2028", "\u0000", "\r", "\n", "\u0085", "\x2029", "\xFEFF",
            })
            {
                string cell = text.Replace("\t", " ");
                RoundTrip(B.Plain(text), B.P("Heading 2", B.En(text)), B.P("Bulleted List", B.En(text)), B.P("Block Quote", B.En(text)), B.P("Example", B.En(text)),
                    B.P("GDR Table Header", B.En(cell)), B.P("GDR Table Row", B.En(cell)));
            }
        }
    }
}
