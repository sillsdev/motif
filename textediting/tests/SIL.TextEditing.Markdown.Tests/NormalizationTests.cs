using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>ADR 0018, owner decision 2026-11-04 (issue 75): Parse returns the text as NFD, the form the editor stores, and every
    /// finding still refers to the source as written.</summary>
    public class NormalizationTests
    {
        const string Acute = "\u0301", DotBelow = "\u0323";

        static string Dump(string markdown) => ModelDump.Of(MarkdownConverter.Parse(markdown, Hosts.Project()).Document);

        [Theory]
        [InlineData("caf\u00E9", "Normal| en\"cafe\u0301\"")]
        [InlineData("# caf\u00E9", "Heading 1| en\"cafe\u0301\"")]
        [InlineData("- caf\u00E9", "Bulleted List| en\"cafe\u0301\"")]
        [InlineData("> caf\u00E9", "Block Quote| en\"cafe\u0301\"")]
        [InlineData("::: {custom-style=Example}\ncaf\u00E9\n:::", "Example| en\"cafe\u0301\"")]
        [InlineData("a **caf\u00E9** b", "Normal| en\"a \" en\"cafe\u0301\"{p3=1:3} en\" b\"")]
        [InlineData("[caf\u00E9]{ws=ceb}", "Normal| ceb\"cafe\u0301\"")]
        [InlineData("| caf\u00E9 | \u00F1 |\n| --- | --- |\n| \u00E9 | x |", "GDR Table Header| en\"cafe\u0301\\x0009n\u0303\"\nGDR Table Row| en\"e\u0301\\x0009x\"")]
        public void PrecomposedTextIsReadAsNfd(string markdown, string expected)
        {
            Assert.Equal(expected, Dump(markdown));
        }

        [Fact]
        public void TheOtherFormsOfOneCharacterReadAlike()
        {
            Assert.Equal(Dump("caf\u00E9"), Dump("cafe" + Acute));
            Assert.Equal(Dump("\uAC00"), Dump("\u1100\u1161")); // a Hangul syllable and its two jamo
        }

        [Fact]
        public void PrecomposedTextInAnAbstractIsReadAsNfd()
        {
            var runs = MarkdownConverter.ParseInline("caf\u00E9 [\u00F1]{ws=ceb}").Runs;
            Assert.Equal("cafe" + Acute + " ", runs[0].Text);
            Assert.Equal("n\u0303", runs[1].Text);
        }

        [Fact]
        public void MarksReorderIntoCanonicalOrder()
        {
            // e-acute then dot-below: NFD puts the dot below (class 220) before the acute (class 230).
            Assert.Equal("Normal| en\"e" + DotBelow + Acute + "\"", Dump("\u00E9" + DotBelow));
        }

        [Fact]
        public void AMarkAtTheEdgeOfASpanStaysInItsOwnRun()
        {
            Assert.Equal("Normal| ceb\"e\" en\"" + Acute + "\"", Dump("[e]{ws=ceb}" + Acute));
            Assert.Equal("Normal| en\"" + Acute + "\" ceb\"e\"", Dump(Acute + "[e]{ws=ceb}"));
            // A precomposed letter in the span, a mark after it: each run is normalised alone, the mark is not moved across the edge.
            Assert.Equal("Normal| ceb\"e" + Acute + "\" en\"" + DotBelow + "\"", Dump("[\u00E9]{ws=ceb}" + DotBelow));
        }

        [Fact]
        public void FindingsRefertoTheSourceAsWritten()
        {
            const string source = "caf\u00E9 \u00F1 [x]{ws=zzz-unknown} \u00E9";
            var result = MarkdownConverter.Parse(source, Hosts.Project());
            var d = result.Diagnostics.Single(x => x.Id == "fwmd.ws.unknown");
            Assert.Equal(source, result.Source);
            Assert.Equal("ws=zzz-unknown", d.Observed);
            Assert.Equal(source.IndexOf("ws=zzz-unknown"), d.Start);
            Assert.Equal(1, d.Line);
            Assert.Equal(source.IndexOf("ws=zzz-unknown") + 1, d.Column);
            // The text is NFD and longer, so an offset into it would not be the same offset.
            Assert.Equal("cafe" + Acute + " n\u0303 ", result.Document.Paragraphs[0].Runs[0].Text);
        }

        [Fact]
        public void TheObjectsOfAParseRefertoTheSourceAsWritten()
        {
            const string source = "caf\u00E9 [\u00E9](https://example.org)";
            var result = MarkdownConverter.Parse(source);
            var link = result.Objects.Single();
            Assert.Equal(source.IndexOf('['), link.Source!.Start);
            Assert.Equal(source.Length, link.Source!.End);
        }

        [Fact]
        public void TextThatIsAlreadyNfdIsReadAsItIs()
        {
            Assert.Equal("Normal| en\"cafe" + Acute + "\"", Dump("cafe" + Acute));
        }

        [Fact]
        public void AnIllFormedSurrogateDoesNotThrow()
        {
            Assert.Equal("Normal| en\"a\\xD800 e" + Acute + "\"", Dump("a\uD800 \u00E9"));
        }

        [Fact]
        public void ToMarkdownWritesTheTextAsStoredAndTheCopyReadsBackEquivalent()
        {
            var nfc = B.Plain("caf\u00E9");
            string markdown = MarkdownConverter.ToMarkdown(B.Doc(nfc), Hosts.Project());
            Assert.Equal("caf\u00E9\n", markdown); // nothing is normalised on the way out
            var back = MarkdownConverter.Parse(markdown).Document.Paragraphs[0];
            Assert.Equal("cafe" + Acute, back.Runs[0].Text);
            Assert.True(MarkdownConverter.AreEquivalent(nfc, back));
        }

        [Fact]
        public void AttributeValuesAreNeverNormalised()
        {
            const string nfc = "caf\u00E9";
            var result = MarkdownConverter.Parse("[caf\u00E9]{s6=\"" + nfc + "\" s2=\"" + nfc + "\" custom-style=\"" + nfc + "\"} [x](https://e.org/" + nfc + ")");
            var runs = result.Document.Paragraphs[0].Runs;
            Assert.Equal("cafe" + Acute, runs[0].Text);
            Assert.Equal(nfc, runs[0].ObjData);
            Assert.Equal(nfc, runs[0].Props.StrProps[2]);
            Assert.Equal(nfc, runs[0].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.EndsWith("https://e.org/" + nfc, runs.Last(r => r.ObjData != null).ObjData);
        }

        [Fact]
        public void NormalisationCanBeSwitchedOff()
        {
            var options = new MarkdownOptions { NormalizeText = false, WriteFrontMatter = false };
            Assert.Equal("caf\u00E9", MarkdownConverter.Parse("caf\u00E9", options).Document.Paragraphs[0].Runs[0].Text);
            Assert.Equal("caf\u00E9", MarkdownConverter.ParseInline("caf\u00E9", options).Runs[0].Text);
            Assert.True(new MarkdownOptions().NormalizeText);
        }
    }
}
