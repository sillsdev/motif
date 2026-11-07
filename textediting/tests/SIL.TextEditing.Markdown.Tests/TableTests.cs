using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Issue 81: how tables are written when cells hold brackets or nothing, what a delimiter row of the wrong width is, and the
    /// size at which a table is written as fences instead.</summary>
    public class TableTests
    {
        static Paragraph Header(string text) => B.P("GDR Table Header", B.En(text));
        static Paragraph Row(string text) => B.P("GDR Table Row", B.En(text));

        static string WriteAndCheck(params Paragraph[] paragraphs)
        {
            var document = B.Doc(paragraphs);
            string markdown = MarkdownConverter.ToMarkdown(document, Hosts.Project());
            var back = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.DoesNotContain(back.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs), markdown);
            return markdown;
        }

        // A bracket one cell leaves open can close in a cell of another row; the table is still one table.
        [Theory]
        [InlineData("a[", "b]{v}")]
        [InlineData("a[", "]{v}")]
        [InlineData("[", "](x)")]
        [InlineData("[x", "y]{v}")]
        [InlineData("a[\tb", "c]{v}\td")]
        public void BracketsOpenedInTheHeaderAndClosedInARowAreStillATable(string header, string row)
        {
            string markdown = WriteAndCheck(Header(header), Row(row), Row("c\td".Substring(0, header.Contains('\t') ? 3 : 1)));
            Assert.StartsWith("| ", markdown);
            Assert.DoesNotContain(":::", markdown);
        }

        [Fact]
        public void BracketsOpenedInOneRowAndClosedInALaterOneKeepTheTableWhole()
        {
            string markdown = WriteAndCheck(Header("a"), Row("{.v}(b["), Row("x"), Row("b]{.v})"));
            Assert.StartsWith("| a |\n|---|\n", markdown);
            Assert.DoesNotContain(":::", markdown);
        }

        [Fact]
        public void ARowThatCannotBeACellIsTheOnlyOneLeftOutOfTheTable()
        {
            var tabInObject = B.P("GDR Table Row", B.R("en", "x\ty", objData: "\u0004http://e.org"));
            string markdown = WriteAndCheck(Header("a"), Row("b"), tabInObject, Row("c"));
            Assert.StartsWith("| a |\n|---|\n| b |", markdown);
        }

        [Fact]
        public void AOneColumnTableWithAnEmptyBodyRowIsOneTable()
        {
            string markdown = WriteAndCheck(Header("a"), B.P("GDR Table Row"), Row("b"), B.P("GDR Table Row"));
            Assert.DoesNotContain(":::", markdown);
            Assert.DoesNotContain("{empty", markdown);
            Assert.Equal(1, markdown.Split('\n').Count(l => l.StartsWith("|---|")));
        }

        [Theory]
        [InlineData("| a | b |\n| --- |\n| 1 | 2 |", 2, 1, 2)]
        [InlineData("| a |\n| --- | --- |\n| 1 |", 2, 2, 1)]
        [InlineData("| a | b |\n| --- | --- | --- |\n| 1 | 2 |", 2, 3, 2)]
        public void ADelimiterRowWithAnotherColumnCountThanTheHeaderIsAFinding(string markdown, int line, int delimiterCells, int headerCells)
        {
            var result = MarkdownConverter.Parse(markdown, Hosts.Project());
            var finding = Assert.Single(result.Diagnostics, d => d.Id == MarkdownDiagnosticIds.TableRagged);
            Assert.Equal(MarkdownSeverity.Error, finding.Severity);
            Assert.Equal(line, finding.Line);
            Assert.Equal(1, finding.Column);
            Assert.Equal(markdown.Split('\n')[line - 1], finding.Observed);
            Assert.Contains("delimiter row has " + delimiterCells + (delimiterCells == 1 ? " cell" : " cells"), finding.Message);
            Assert.Contains("the header has " + headerCells, finding.Message);
            Assert.Equal("|" + string.Concat(Enumerable.Repeat("---|", headerCells)), finding.Replacement);
        }

        [Theory]
        [InlineData("| a | b |\n| --- | --- |\n| 1 | 2 |")]
        [InlineData("a | b\n--- | ---\n1 | 2")]
        [InlineData("| a |\n|---|\n| 1 |")]
        public void ADelimiterRowOfTheHeadersWidthIsNoFinding(string markdown)
            => Assert.Empty(MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics);

        // Markdig reads a table in time and depth that grow with rows times (columns + 1); past about 10,200 it gives up, the parser
        // reports one fwmd.syntax.too-deep and keeps the lines as text, and the writer writes the table as fences, which read back.
        [Trait("Size", "Large")]
        [Fact]
        public void AOneColumnTableJustUnderTheLimitIsWrittenAsATable()
        {
            var paragraphs = new Paragraph[5000];
            paragraphs[0] = Header("a");
            for (int i = 1; i < paragraphs.Length; i++) paragraphs[i] = Row("b");
            string markdown = WriteAndCheck(paragraphs);
            Assert.StartsWith("| a |\n|---|\n", markdown);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void AOneColumnTableOverTheLimitIsWrittenAsFencesAndReadsBack()
        {
            var paragraphs = new Paragraph[5300];
            paragraphs[0] = Header("a");
            for (int i = 1; i < paragraphs.Length; i++) paragraphs[i] = Row("b");
            string markdown = WriteAndCheck(paragraphs);
            Assert.DoesNotContain("|---|", markdown);
            Assert.StartsWith("::: {custom-style=\"GDR Table Header\"}\na\n:::", markdown);
        }
    }
}
