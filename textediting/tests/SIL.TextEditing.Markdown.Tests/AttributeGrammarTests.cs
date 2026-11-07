using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Spec 3.3: the grammar of attribute blocks, read through the parser. The spellings and findings that the conformance fixtures spell out are not repeated; these tests
    /// are the sweeps (every property number and value, every string property) and the finding's places.</summary>
    public class AttributeGrammarTests
    {
        static string Dump(string markdown) => ModelDump.Of(MarkdownConverter.Parse(markdown, Hosts.Project()).Document);
        static string[] Ids(string markdown) => MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics.Select(d => d.Id).ToArray();

        [Theory]
        [InlineData("[x]{s2=a-b_c.d#e}", "Normal| en\"x\"{s2=a-b_c.d#e}")]
        [InlineData("[x]{s2=\"a b\"}", "Normal| en\"x\"{s2=a b}")]
        [InlineData("[x]{s2=\"\"}", "Normal| en\"x\"{s2=}")]
        [InlineData("[x]{s2=\"a\\\"b\\\\c\"}", "Normal| en\"x\"{s2=a\\\"b\\\\c}")]
        [InlineData("[x]{s2=\"}{ = |\"}", "Normal| en\"x\"{s2=}{ = |}")]
        [InlineData("[x]{s2=a:b/c}", "Normal| en\"x\"{s2=a:b/c}")]
        [InlineData("[x]{s2=\"a\\q\"}", "Normal| en\"x\"{s2=a\\\\q}")]
        [InlineData("[x]{s2='single'}", "Normal| en\"x\"{s2=single}")]
        [InlineData("[x]{ws=ceb  bold=on}", "Normal| ceb\"x\"{p3=1:3}")]
        public void ValuesAreBareOrQuotedWithTheTwoEscapes(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Theory]
        [InlineData("[x]{}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{ws}", "fwmd.syntax.unknown-attribute")]
        [InlineData("[x]{ws=}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{=a}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{1a=b}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{#id}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{.}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{s2=a,b}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{s2=\"open}", "fwmd.syntax.unclosed-mark")]
        [InlineData("[x]{s2=a", "fwmd.syntax.unclosed-mark")]
        [InlineData("[x]{", "fwmd.syntax.unclosed-mark")]
        [InlineData("[x]{Bold=on}", "fwmd.syntax.unknown-attribute")]
        [InlineData("[x]{bold=on bold=off}", "fwmd.syntax.duplicate-attribute")]
        [InlineData("[x]{.v ws=ceb}", "fwmd.syntax.duplicate-attribute")]
        [InlineData("[x]{custom-style=A custom-style=B}", "fwmd.syntax.duplicate-attribute")]
        [InlineData("[x]{style=x}", "fwmd.syntax.unknown-attribute")]
        [InlineData("[x]{cs=x}", "fwmd.syntax.unknown-attribute")]
        [InlineData("[x]{.empty}", "fwmd.syntax.unsupported-markup")]
        [InlineData("[x]{fontsize=big}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{fontsize=1 fontsizeUnit=furlong}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{forecolor=12345}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{p50=1:wide}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{p50=1:99999999999}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{fontsizeUnit=rel}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{s6x=00A}", "fwmd.syntax.malformed-attribute")]
        [InlineData("[x]{s2x=zzzz}", "fwmd.syntax.malformed-attribute")]
        public void BadBlocksAreReportedWithTheRightId(string markdown, string id) => Assert.Contains(id, Ids(markdown));

        [Fact]
        public void KeysAreCaseSensitive() => Assert.Contains("fwmd.syntax.unknown-attribute", Ids("[x]{FontSize=1}"));

        [Fact]
        public void TheLiblcmSpellingOfMarginTopIsAcceptedAndNeverWritten()
        {
            Assert.Equal(Dump("[x]{MarginTop=5}"), Dump("[x]{marginTop=5}"));
            Assert.Contains("MarginTop=5", MarkdownConverter.Canonicalize("[x]{marginTop=5}", Hosts.Project()));
        }

        [Fact]
        public void TwoSpellingsOfOnePropertyAreADuplicate() => Assert.Contains("fwmd.syntax.duplicate-attribute", Ids("[x]{MarginTop=1 marginTop=2}"));

        [Fact]
        public void ColorsTakeHexOrNamesAndAreWrittenAsHash()
        {
            Assert.Equal(Dump("[x]{forecolor=ff0000}"), Dump("[x]{forecolor=red}"));
            Assert.Equal(Dump("[x]{forecolor=0000ff}"), Dump("[x]{forecolor=#0000FF}"));
            Assert.Equal("Normal| en\"x\"{p8=16711680:0}", Dump("[x]{forecolor=0000ff}"));
            Assert.Contains("forecolor=#0000ff", MarkdownConverter.Canonicalize("[x]{forecolor=0000ff}", Hosts.Project()));
        }

        [Fact]
        public void AnErrorInOneItemDoesNotHideTheNext()
        {
            var ids = Ids("[x]{wss=ceb bold=maybe ws=zzz-unknown}");
            Assert.Contains("fwmd.syntax.unknown-attribute", ids);
            Assert.Contains("fwmd.syntax.malformed-attribute", ids);
            Assert.Contains("fwmd.ws.unknown", ids);
        }

        [Fact]
        public void FindingsCarryTheItemsPlaceAndAHintWhereThereIsOne()
        {
            var result = MarkdownConverter.Parse("see [x]{wss=ceb}", Hosts.Project());
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal(1, d.Line);
            Assert.Equal(9, d.Column);
            Assert.Equal("wss=ceb", d.Observed);
            Assert.Equal(new[] { "ws" }, d.Choices);
        }

        [Fact]
        public void AClassHintNamesTheClass()
        {
            var d = Assert.Single(MarkdownConverter.Parse("[x]{v}", Hosts.Project()).Diagnostics);
            Assert.Equal("fwmd.syntax.unknown-attribute", d.Id);
            Assert.Equal(new[] { ".v" }, d.Choices);
        }

        [Fact]
        public void EveryNamedKeyAndEveryFallbackRoundTripsThroughTheNotation()
        {
            for (int number = 1; number <= 170; number++)
            {
                foreach (int variation in new[] { 0, 1, 2, 3, 15, 7 })
                {
                    foreach (int value in new[] { 0, 1, 2, -1, 12000, int.MaxValue, int.MinValue, -1073741824, 255, 16777215, 16777216 })
                    {
                        var run = B.R("en", "x", p => p.SetInt(number, value, variation));
                        var document = B.Doc(B.P("Normal", run));
                        var options = Hosts.Project();
                        string text = MarkdownConverter.ToMarkdown(document, options);
                        var back = MarkdownConverter.Parse(text, options);
                        Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs), "p" + number + " " + value + ":" + variation + " -> " + text);
                        Assert.DoesNotContain(back.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
                    }
                }
            }
        }

        [Theory]
        [InlineData("[x]{.v=1}")]
        [InlineData("[x]{ws}")]
        [InlineData("[x]{custom-style}")]
        [InlineData("[x]{guid}")]
        [InlineData("[x]{bold}")]
        [InlineData("[x]{p3}")]
        [InlineData("[x]{s2}")]
        public void AKeyWrittenWithNoValueOrAClassWrittenWithOneIsAnUnknownAttributeOrMalformed(string markdown)
        {
            var d = Assert.Single(MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics, x => x.Severity == MarkdownSeverity.Error);
            Assert.True(d.Id == "fwmd.syntax.unknown-attribute" || d.Id == "fwmd.syntax.malformed-attribute", d.Id);
        }

        [Theory]
        [InlineData("[x]{.v ws=ceb}", ".v")]
        [InlineData("[x]{ws=ceb .v}", ".v")]
        [InlineData("[]{.empty .v ws=ceb}", ".v")]
        public void VAndWsTogetherNameTheWritingSystemTwice(string markdown, string observed)
        {
            var d = Assert.Single(MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics);
            Assert.Equal("fwmd.syntax.duplicate-attribute", d.Id);
            Assert.Equal(observed, d.Observed);
        }

        [Theory]
        [InlineData("[x]{}")]
        [InlineData("[x]{ws=}")]
        [InlineData("[x]{1a=b}")]
        [InlineData("[x]{bold=maybe}")]
        [InlineData("[x]{fontsize=big}")]
        [InlineData("[x]{guid=nope}")]
        [InlineData("[x]{fontsizeUnit=rel}")]
        public void AGrammarErrorOrAValueThatDoesNotFitItsKeyIsMalformed(string markdown)
            => Assert.Contains(MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics, d => d.Id == "fwmd.syntax.malformed-attribute");

        [Fact]
        public void EveryStringPropertyRoundTripsThroughTheNotation()
        {
            foreach (int number in new[] { 1, 2, 3, 4, 5, 7, 130, 145, 146, 147, 156, 999 })
            {
                foreach (string value in new[] { "", "a", "a b", "q\"uote", "back\\slash", "}{", "|", "é", "\x0001", "\x2028", "none", "x=y", " lead", "a&amp;b", "&#x41;", "a\tb", "\uD800" })
                {
                    var run = B.R("en", "x", p => p.SetStr(number, value));
                    var document = B.Doc(B.P("Normal", run));
                    var options = Hosts.Project();
                    string text = MarkdownConverter.ToMarkdown(document, options);
                    var back = MarkdownConverter.Parse(text, options);
                    Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs), "s" + number + " '" + value + "' -> " + text);
                }
            }
        }

        [Fact]
        public void AStringWithAControlCharacterOrALoneSurrogateIsHexWhateverItsName()
        {
            string text = MarkdownConverter.ToMarkdown(B.Doc(B.P("Normal", B.R("en", "x", p => { p.SetStr(1, "a\tb"); p.SetStr(147, "\uD800"); p.SetStr(2, "plain"); }))), Hosts.Project());
            Assert.Equal("[x]{s1x=006100090062 s2=plain s147x=D800}\n", text);
        }
    }
}
