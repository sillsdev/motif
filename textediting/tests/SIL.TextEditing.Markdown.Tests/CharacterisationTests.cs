using System.Linq;
using SIL.TextEditing.Markdown.Reader;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Characterisation tests from the review research: they pin what the reader and the writer do today, in places a change to
    /// Markdig or to the writer's search could quietly break. They are expected to pass.</summary>
    public class CharacterisationTests
    {
        const string Acute = "\u0301";

        static void RoundTrips(params Paragraph[] paragraphs)
        {
            var document = B.Doc(paragraphs);
            var options = Hosts.Project();
            string markdown = MarkdownConverter.ToMarkdown(document, options);
            var back = MarkdownConverter.Parse(markdown, options);
            Assert.DoesNotContain(back.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs), markdown + "\n" + ModelDump.Of(back.Document));
            Assert.Equal(markdown, MarkdownConverter.ToMarkdown(back.Document, options));
        }

        // Two runs of one format that differ in another property must not be read as one: **a****b** is not two bold runs.
        [Fact]
        public void TwoAdjacentBoldRunsInDifferentWritingSystemsRoundTrip()
            => RoundTrips(B.P("Normal", B.BoldRun("en", "a"), B.BoldRun("ceb", "b")));

        [Fact]
        public void TwoAdjacentBoldRunsDifferingInFontSizeRoundTrip()
            => RoundTrips(B.P("Normal", B.BoldRun("en", "a"), B.R("en", "b", p =>
            {
                p.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn, TextPropertyNumbers.VariationEnum);
                p.SetInt(6, 14000, 1);
            })));

        [Fact]
        public void AdjacentBoldAndItalicRunsAndBoldRunsOfTwoStylesRoundTrip()
            => RoundTrips(B.P("Normal", B.BoldRun("en", "a"), B.ItalicRun("en", "b"), B.BoldRun("en", "c"),
                B.R("en", "d", p =>
                {
                    p.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn, TextPropertyNumbers.VariationEnum);
                    p.SetStr(TextPropertyNumbers.NamedStyle, "Strong");
                })));

        [Fact]
        public void APipeInsideASpanOrAQuotedAttributeValueInATableCellRoundTrips()
        {
            RoundTrips(B.P("GDR Table Header", B.En("h"), B.En("\t"), B.En("i")),
                B.P("GDR Table Row", B.R("ceb", "x|y"), B.En("\t"), B.Styled("en", "z", "a|b")),
                B.P("GDR Table Row", B.R("ceb", "|"), B.En("\t"), B.Styled("en", "|", "|")));
        }

        [Fact]
        public void APipeInAQuotedAttributeValueDoesNotEndTheCell()
        {
            var result = MarkdownConverter.Parse("| a | b |\n| --- | --- |\n| [x]{custom-style=\"p|q\"} | y |", Hosts.Project());
            Assert.Empty(result.Diagnostics);
            Assert.Equal("GDR Table Row", result.Document.Paragraphs[1].Style);
            Assert.Equal("x\ty", result.Document.Paragraphs[1].Text);
            Assert.Equal("p|q", result.Document.Paragraphs[1].Runs[0].Props.StrProps[TextPropertyNumbers.NamedStyle]);
        }

        // CommonMark: a closing delimiter run is right-flanking when it is not preceded by white space; what follows it does not matter
        // unless it is white space or punctuation. A combining mark is neither, so the emphasis closes.
        [Theory]
        [InlineData("**a**" + Acute, "Normal| en\"a\"{p3=1:3} en\"" + Acute + "\"")]
        [InlineData("*a*" + Acute, "Normal| en\"a\"{p2=1:3} en\"" + Acute + "\"")]
        [InlineData("***a***" + Acute, "Normal| en\"a\"{p2=1:3,p3=1:3} en\"" + Acute + "\"")]
        [InlineData("x**a**" + Acute + "y", "Normal| en\"x\" en\"a\"{p3=1:3} en\"" + Acute + "y\"")]
        public void ACombiningMarkRightAfterAClosingStarEndsTheEmphasis(string markdown, string expected)
            => Assert.Equal(expected, ModelDump.Of(MarkdownConverter.Parse(markdown, new MarkdownOptions { NormalizeText = false }).Document));

        [Fact]
        public void ACombiningMarkAfterABoldOrItalicRunRoundTrips()
        {
            RoundTrips(B.P("Normal", B.BoldRun("en", "a"), B.En(Acute + "b")));
            RoundTrips(B.P("Normal", B.ItalicRun("en", "a"), B.En(Acute)));
            RoundTrips(B.P("Normal", B.En("a"), B.BoldRun("en", Acute + "b")));
        }

        // What Markdig would read as markup if an extension were on. Text the notation does not spell is text.
        [Theory]
        [InlineData("# Heading {#id .class}")]
        [InlineData("text {#id .class key=value}")]
        [InlineData("note[^1]\n\n[^1]: the note")]
        [InlineData("- [x] done\n- [ ] todo")]
        [InlineData(":smile: and :+1:")]
        [InlineData("~~struck~~ ==marked== ++inserted++")]
        [InlineData("Term\n: definition")]
        [InlineData("*[HTML]: Hyper Text Markup Language\n\nHTML")]
        [InlineData("$x^2$ and $$y$$")]
        [InlineData("\"\"cited\"\" <<a>>")]
        [InlineData("^^^\nfigure\n^^^")]
        [InlineData("1. a\n   1. nested\n\nA. alpha\nB. beta")]
        [InlineData("http://example.org and www.example.org and a@b.org")]
        [InlineData("a -- b --- c \"quoted\" 'single' ... <<x>>")]
        public void MarkdigsAdvancedExtensionsAreNotEnabled(string markdown)
        {
            var result = MarkdownConverter.Parse(markdown);
            string text = string.Concat(result.Document.Paragraphs.Select(p => p.Text + "\n"));
            // Each of these either stays exactly as written, or is a finding the notation already makes (an unsupported block or a list).
            foreach (string marker in new[] { "{#id", "[^1]", "~~", "==", "H~2~O", "x^2^", "++", "$x^2$", "\"\"cited\"\"", "<<a>>", ":smile:", "^^^", "http://example.org", "www.example.org", "a@b.org", "a -- b --- c" })
                if (markdown.Contains(marker) && !result.Diagnostics.Any(d => d.Severity == MarkdownSeverity.Error))
                    Assert.Contains(marker, text);
            Assert.DoesNotContain(result.Document.Paragraphs, p => p.Runs.Any(r => r.ObjData != null));
        }

        // The extension list is the guard: any further extension in the core pipelines changes what a text means, so adding one must fail here.
        [Fact]
        public void TheCorePipelinesHoldOnlyPipeTablesAndTheNotation()
        {
            string[] document = NotationExtension.Document.Extensions.Select(e => e.GetType().Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "NotationExtension", "PipeTableExtension" }, document);
            string[] inline = NotationExtension.InlineOnly.Extensions.Select(e => e.GetType().Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "NotationExtension" }, inline);
        }
    }
}
