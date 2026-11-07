using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Every finding carries its exact line, column and the text it is about, in every kind of block.</summary>
    public class PositionTests
    {
        static void AssertFindings(string markdown, int line, params (string id, string observed)[] expected)
        {
            var result = MarkdownConverter.Parse(markdown, Hosts.Project());
            string[] lines = markdown.Split('\n');
            foreach (var (id, observed) in expected)
            {
                var found = result.Diagnostics.Where(d => d.Id == id && d.Observed == observed).ToList();
                Assert.True(found.Count == 1, id + " '" + observed + "' in:\n" + string.Join("\n", result.Diagnostics));
                var d = found[0];
                int column = lines[line - 1].IndexOf(observed, System.StringComparison.Ordinal) + 1;
                Assert.Equal(line, d.Line);
                Assert.Equal(column, d.Column);
                Assert.Equal(line, d.EndLine);
                Assert.Equal(column + observed.Length, d.EndColumn);
            }
        }

        const string Marks = "`x` <b> <http://x.y> [n](/lexicon/nope) ![c](/objects/00000000-0000-0000-0000-000000000000) [y]{ws=zzz-unknown} [z]{wss=1}";

        static readonly (string, string)[] MarkFindings =
        {
            ("fwmd.syntax.unsupported-markup", "`x`"),
            ("fwmd.syntax.unsupported-markup", "<b>"),
            ("fwmd.syntax.unsupported-markup", "<http://x.y>"),
            ("fwmd.link.not-found", "[n](/lexicon/nope)"),
            ("fwmd.picture.unknown", "![c](/objects/00000000-0000-0000-0000-000000000000)"),
            ("fwmd.ws.unknown", "ws=zzz-unknown"),
            ("fwmd.syntax.unknown-attribute", "wss=1"),
        };

        [Fact]
        public void InAParagraphAfterAnother() => AssertFindings("a\n\n" + Marks, 3, MarkFindings);

        [Fact]
        public void InAListItem() => AssertFindings("a\n\n- " + Marks, 3, MarkFindings);

        [Fact]
        public void InAQuote() => AssertFindings("a\n\n> " + Marks, 3, MarkFindings);

        [Fact]
        public void InAHeading() => AssertFindings("a\n\n# " + Marks, 3, MarkFindings);

        [Fact]
        public void InAFenceLine() => AssertFindings("a\n\n::: {custom-style=S}\n" + Marks + "\n:::", 4, MarkFindings);

        [Fact]
        public void InATableCellAfterAParagraph() => AssertFindings("a\n\n| " + Marks.Replace("|", "x") + " |\n| --- |", 3, MarkFindings);

        [Fact]
        public void InTheCellsOfASecondTable()
            => AssertFindings("| h |\n| --- |\n\nb\n\n| x | `x` | [n](/lexicon/nope) | [y]{ws=zzz-unknown} |\n| --- | --- | --- | --- |", 6,
                ("fwmd.syntax.unsupported-markup", "`x`"), ("fwmd.link.not-found", "[n](/lexicon/nope)"), ("fwmd.ws.unknown", "ws=zzz-unknown"));

        [Fact]
        public void InABodyRowOfATable()
            => AssertFindings("a\n\n| h | h |\n| --- | --- |\n| `x` | [n](/lexicon/nope) |", 5,
                ("fwmd.syntax.unsupported-markup", "`x`"), ("fwmd.link.not-found", "[n](/lexicon/nope)"));

        // A link that cannot be resolved is a finding on the whole link, attributes included.
        const string TextLink = "[see](/lexicon/zzz)";
        const string TextLinkWithUrl = "[see this](/lexicon/zzz){tool=x}";

        [Fact]
        public void ATextFormLinkInAParagraph() => AssertFindings("a\n\nb " + TextLink + " c", 3, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkWithAUrlInAParagraph() => AssertFindings("a\n\nb " + TextLinkWithUrl + " c", 3, ("fwmd.link.not-found", TextLinkWithUrl));

        [Fact]
        public void ATextFormLinkOnTheSecondLineOfAParagraph() => AssertFindings("a\n\nfirst line\nsecond " + TextLink + " c", 4, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkOnTheContinuationLineOfAListItem() => AssertFindings("a\n\n- first\n  b " + TextLink, 4, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInAQuote() => AssertFindings("a\n\n> b " + TextLink, 3, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInAHeading() => AssertFindings("a\n\n# b " + TextLink, 3, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInAFenceLine() => AssertFindings("a\n\n::: {custom-style=S}\nb " + TextLink + " c\n:::", 4, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInATableCell() => AssertFindings("a\n\n| h | h |\n| --- | --- |\n| x | b " + TextLink + " c |", 5, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInTheHeaderOfASecondTable() => AssertFindings("| h |\n| --- |\n\nb\n\n| x " + TextLink + " | y |\n| --- | --- |", 6, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInACellNextToAnotherSpan() => AssertFindings("a\n\n| [p]{.v} | " + TextLink + " |\n| --- | --- |", 3, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInALaterRowOfATableAfterAFence()
            => AssertFindings("::: {custom-style=S}\nq\n:::\n\n| h | h |\n| --- | --- |\n| a | b |\n| c | d " + TextLink + " |", 8, ("fwmd.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkWithCrlfLineEnds()
        {
            var d = Assert.Single(MarkdownConverter.Parse("a\r\n\r\nb " + TextLink + " c\r\n", Hosts.Project()).Diagnostics);
            Assert.Equal((3, 3, 3, 3 + TextLink.Length), (d.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.Equal(TextLink, d.Observed);
        }

        [Fact]
        public void ANestedSpanInsideATextFormLinkDoesNotMoveIt() => AssertFindings("a\n\nb [see [c]{.v} d](/lexicon/zzz) e", 3, ("fwmd.link.not-found", "[see [c]{.v} d](/lexicon/zzz)"));

        [Fact]
        public void ATextFormLinkTheHostResolvesRaisesNothing()
        {
            var host = new TestObjects();
            var guid = new System.Guid("11111111-2222-3333-4444-555555555555");
            host.Names[guid] = "palit";
            Assert.Empty(MarkdownConverter.Parse("a\n\n[see](/lexicon/palit){guid=" + guid + "}", Hosts.Project(host)).Diagnostics);
        }

        [Theory]
        [InlineData("a `x` b", "\\`x\\`")]
        [InlineData("a <b>x</b> c", "\\<b>")]
        [InlineData("a <http://x.y> c", "\\<http://x.y>")]
        [InlineData("<div>x</div>", "\\<div>x\\</div>")]
        public void UnsupportedMarkupComesWithTheEscapeAsTheFix(string markdown, string fix)
        {
            var result = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.Contains(result.Diagnostics, d => d.Id == "fwmd.syntax.unsupported-markup" && d.Replacement == fix);
        }

        [Fact]
        public void AnUnclosedBlockComesWithTheClosingBraceAsTheFix()
        {
            var d = Assert.Single(MarkdownConverter.Parse("see [kaon]{ws=ceb-x-old", Hosts.Project()).Diagnostics);
            Assert.Equal("{ws=ceb-x-old", d.Observed);
            Assert.Equal("{ws=ceb-x-old}", d.Replacement);
        }

        static MarkdownDiagnostic Unclosed(string markdown, string id = "fwmd.syntax.unclosed-mark")
            => Assert.Single(MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics, d => d.Id == id);

        // The spec's own example, in a real file: the block that lacks its } ends at the end of the line, not after the line end.
        [Theory]
        [InlineData("line1\n[kaon]{ws=ceb-x-old\nline3\n")]
        [InlineData("line1\n[kaon]{ws=ceb-x-old\n")]
        [InlineData("line1\n[kaon]{ws=ceb-x-old")]
        [InlineData("line1\r\n[kaon]{ws=ceb-x-old\r\nline3\r\n")]
        [InlineData("line1\r[kaon]{ws=ceb-x-old\rline3\r")]
        public void AnUnclosedBlockEndsBeforeItsLineEnd(string markdown)
        {
            var d = Unclosed(markdown);
            Assert.Equal("{ws=ceb-x-old", d.Observed);
            Assert.Equal("{ws=ceb-x-old}", d.Replacement);
            Assert.Equal((2, 7, 2, 20), (d.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.Equal(markdown.IndexOf("{ws", System.StringComparison.Ordinal) + 13, d.End);
        }

        [Theory]
        [InlineData("::: {custom-style=S}\n[kaon]{ws=ceb-x-old\n:::\n", 2)]
        [InlineData("::: {custom-style=S}\r\n[kaon]{ws=ceb-x-old\r\n:::\r\n", 2)]
        [InlineData("a\n\n- x\n  [kaon]{ws=ceb-x-old\n", 4)]
        [InlineData("a\n\n> [kaon]{ws=ceb-x-old\n", 3)]
        [InlineData("a\n\n| h |\n| --- |\n| [kaon]{ws=ceb-x-old\n", 5)]
        public void AnUnclosedBlockEndsBeforeItsLineEndInEveryBlock(string markdown, int line)
        {
            var d = Unclosed(markdown);
            Assert.Equal("{ws=ceb-x-old", d.Observed);
            Assert.Equal("{ws=ceb-x-old}", d.Replacement);
            Assert.Equal(line, d.Line);
            Assert.Equal(line, d.EndLine);
        }

        [Theory]
        [InlineData("[palit](/lexicon/palit){guid=1\nmore\n", "{guid=1", "{guid=1}")]
        [InlineData("[t](http://x){ws=ceb\nmore\n", "{ws=ceb", "{ws=ceb}")]
        [InlineData("::: {custom-style=S\nx\n:::\n", "{custom-style=S", "{custom-style=S}")]
        [InlineData("[]{.empty ws=ceb\n", "{.empty ws=ceb", "{.empty ws=ceb}")]
        public void AnUnclosedBlockOfEveryKindEndsBeforeItsLineEnd(string markdown, string observed, string fix)
        {
            var d = MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics.First(x => x.Id == "fwmd.syntax.unclosed-mark");
            Assert.Equal(observed, d.Observed);
            Assert.Equal(d.Line, d.EndLine);
            Assert.DoesNotContain('\n', d.Observed!);
            if (fix != null) Assert.Equal(fix, d.Replacement);
        }

        // Adding } is an exact fix only when what is left is known items: stray words would become flags.
        [Theory]
        [InlineData("see [kaon]{ws=ceb-x-old and more\n", "{ws=ceb-x-old and more")]
        [InlineData("see [kaon]{ws=\n", "{ws=")]
        [InlineData("see [kaon]{cs=\"Strong\n", "{cs=\"Strong")]
        public void AnUnclosedBlockWhoseClosingBraceWouldNotFixItHasNoReplacement(string markdown, string observed)
        {
            var d = Unclosed(markdown);
            Assert.Equal(observed, d.Observed);
            Assert.Null(d.Replacement);
        }

        // fwmd.table.alignment is about the delimiter cell, one finding per aligned column: replacing the observed text with the
        // replacement is the whole fix.
        static (string observed, int line, int column)[] Alignments(string markdown)
            => MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics.Where(d => d.Id == "fwmd.table.alignment")
                .Select(d =>
                {
                    Assert.Equal("---", d.Replacement);
                    Assert.Equal(d.Line, d.EndLine);
                    Assert.Equal(d.Column + d.Observed!.Length, d.EndColumn);
                    return (d.Observed!, d.Line, d.Column);
                }).ToArray();

        [Fact]
        public void AnAlignedColumnIsReportedAtItsDelimiterCell()
            => Assert.Equal(new[] { (":---:", 2, 3) }, Alignments("| a | b |\n| :---: | --- |\n| 1 | 2 |\n"));

        [Fact]
        public void EveryAlignedColumnHasAFindingOfItsOwn()
            => Assert.Equal(new[] { (":---", 2, 3), (":---:", 2, 10), ("---:", 2, 24) }, Alignments("| a | b | c | d |\n| :--- | :---: | --- | ---: |\n| 1 | 2 |"));

        [Theory]
        [InlineData("a | b\n:--- | ---:\n1 | 2", 2, 1)]
        [InlineData("  | a | b |\n  | :--- | --- |\n  | 1 | 2 |", 2, 5)]
        [InlineData("x\n\n| a | b |\n| --- | :-: |\n| 1 | 2 |", 4, 9)]
        public void ADelimiterRowWithoutOuterPipesOrIndentedIsPlacedExactly(string markdown, int line, int column)
        {
            var found = Alignments(markdown);
            Assert.Equal(line, found[0].line);
            Assert.Equal(column, found[0].column);
        }

        [Fact]
        public void TheAlignmentOfASecondTableIsPlacedOnItsOwnLine()
            => Assert.Equal(new[] { (":---:", 7, 3) }, Alignments("| h |\n| --- |\n\nb\n\n| a | b |\n| :---: | --- |\n| 1 | 2 |"));

        [Fact]
        public void TheAlignmentFindingWorksWithCrlf()
            => Assert.Equal(new[] { ("--:", 2, 9) }, Alignments("| a | b |\r\n| --- | --: |\r\n| 1 | 2 |\r\n"));

        [Fact]
        public void ApplyingTheAlignmentFixLeavesNoFinding()
        {
            string text = "| a | b |\n| :---: | ---: |\n| 1 | 2 |\n";
            string fixedText = text;
            foreach (var d in MarkdownConverter.Parse(text, Hosts.Project()).Diagnostics.Where(d => d.Id == "fwmd.table.alignment").OrderByDescending(d => d.Start))
                fixedText = fixedText.Substring(0, d.Start) + d.Replacement + fixedText.Substring(d.End);
            Assert.Equal("| a | b |\n| --- | --- |\n| 1 | 2 |\n", fixedText);
            Assert.Empty(MarkdownConverter.Parse(fixedText, Hosts.Project()).Diagnostics);
        }

        [Fact]
        public void FindingsAreInSourceOrder()
        {
            var result = MarkdownConverter.Parse("`a`\n\n[x]{wss=1}\n\n<b>\n\n[y]{ws=zzz-unknown}", Hosts.Project());
            Assert.Equal(new[] { 1, 3, 5, 7 }, result.Diagnostics.Select(d => d.Line).ToArray());
        }

        [Fact]
        public void AQuotedValueThatLooksLikeALinkInAnEmptyMarkerRaisesNothing()
        {
            var result = MarkdownConverter.Parse("[]{.empty custom-style=\"Heading 1\" .v s2=\"[x](y){a,b} \" s147=mang}", Hosts.Project());
            Assert.Empty(result.Diagnostics);
            Assert.Equal("[x](y){a,b} ", result.Document.Paragraphs[0].Props.StrProps[2]);
        }

        [Fact]
        public void ABrokenBlockHasItsPlaceToo()
        {
            var result = MarkdownConverter.Parse("a\n\n| h | h |\n| --- | --- |\n| [x]{ws=ceb | y |", Hosts.Project());
            Assert.Contains(result.Diagnostics, d => d.Id == "fwmd.syntax.malformed-attribute" && d.Line == 5 && d.Column == 6);
        }
    }
}
