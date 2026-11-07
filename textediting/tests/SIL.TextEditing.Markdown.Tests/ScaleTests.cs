using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using SIL.TextEditing.Markdown.Writer;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Big inputs: the parser returns findings and the writer returns text, in time that grows with the input. The checks look at
    /// what comes back, never at the clock; an input that would take minutes in quadratic time is what makes a slow path visible.</summary>
    public class ScaleTests
    {
        [Fact]
        public void ManyFindingsAndManyEmptyMarkersAreReadInOnePass()
        {
            const int count = 20_000;
            string text = string.Concat(Enumerable.Repeat("[x]{wss=1}\n\n", count)) + string.Concat(Enumerable.Repeat("[]{.empty}\n\n", count));
            var result = MarkdownConverter.Parse(text);
            Assert.Equal(count, result.Diagnostics.Count(d => d.Id == "fwmd.syntax.unknown-attribute"));
            Assert.Equal(count, result.Document.Paragraphs.Count(p => p.Runs.Count == 0));
            Assert.Equal(2 * count, result.Document.Paragraphs.Count);
        }

        [Fact]
        public void AnEmptyMarkerDropsTheFindingsMarkdigMadeWhileReadingItsQuotedValues()
        {
            // A quoted value that looks like a span or a link draws findings while Markdig reads the line as text; the marker is read from
            // the raw line, so they go, and only the findings of the lines around it stay.
            string text = "[a]{wss=1}\n\n[]{.empty s2=\"[x]{wss=2} [y]{ws=\" .v}\n\n[b]{wss=3}\n\n[]{.empty s2=\"[z](q){a,b}\"}\n\n[c]{wss=4}";
            var result = MarkdownConverter.Parse(text);
            Assert.Equal(new[] { 1, 5, 9 }, result.Diagnostics.Select(d => d.Line).ToArray());
            Assert.Equal(5, result.Document.Paragraphs.Count);
        }

        // ------------------------------------------------------------ hostile and very large input: findings, never a crash

        /// <summary>Runs on a thread with the default 1 MB stack, so a recursion that grows with the input fails whatever stack the test runner has.</summary>
        static T OnSmallStack<T>(Func<T> work)
        {
            T result = default!;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { result = work(); }
                catch (Exception e) { error = e; }
            }, 1024 * 1024);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }

        static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

        static MarkdownParseResult ParseSafely(string markdown) => OnSmallStack(() => MarkdownConverter.Parse(markdown));

        static string AllText(MarkdownParseResult result) => string.Join("\n", result.Document.Paragraphs.Select(p => p.Text));

        [Trait("Size", "Large")]
        [Fact]
        public void ThreeThousandUnmatchedBracketsAreText()
        {
            string text = Repeat("[", 3000);
            var result = ParseSafely(text);
            Assert.Equal(text, AllText(result));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void ThreeThousandNestedSpansAreRead()
        {
            var result = ParseSafely(Repeat("[", 3000) + "x" + Repeat("]{.v}", 3000));
            Assert.False(result.HasErrors);
            Assert.Equal("x", AllText(result));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void ThreeThousandNestedEmphasesAreRead()
        {
            var result = ParseSafely(Repeat("*a ", 3000) + "x" + Repeat("* ", 3000));
            Assert.NotEmpty(result.Document.Paragraphs);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void ThreeThousandUnmatchedBracketsInAFenceLineAndATableCellAreText()
        {
            string brackets = Repeat("[", 3000);
            var fence = ParseSafely("::: {custom-style=S}\n" + brackets + "\n:::");
            Assert.Equal(brackets, AllText(fence));
            var table = ParseSafely("| a |\n| --- |\n| " + brackets + " |");
            Assert.Equal("a\n" + brackets, AllText(table));
        }

        static void AssertTooDeep(MarkdownParseResult result, string firstLine)
        {
            var finding = Assert.Single(result.Diagnostics, d => d.Id == MarkdownDiagnosticIds.TooDeep);
            Assert.Equal(MarkdownSeverity.Error, finding.Severity);
            Assert.Equal(1, finding.Line);
            Assert.Equal(1, finding.Column);
            Assert.Equal(firstLine, finding.Observed);
            Assert.Contains("Split", finding.Message);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void OneHundredAndThirtyNestedQuotesAreOneFindingAndLiteralText()
        {
            string line = Repeat("> ", 130) + "x";
            var result = ParseSafely(line);
            AssertTooDeep(result, line);
            Assert.Equal(line, AllText(result));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void AListNestedOneHundredDeepIsOneFindingAndLiteralText()
        {
            string text = string.Join("\n", Enumerable.Range(0, 100).Select(i => new string(' ', 2 * i) + "- x"));
            var result = ParseSafely(text);
            Assert.Contains(result.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.Contains("x", AllText(result));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void ATableOfFiveThousandRowsIsOneFindingAndItsLinesAreText()
        {
            string text = "| a | b |\n| --- | --- |\n" + Repeat("| c | d |\n", 5000);
            var result = ParseSafely(text);
            AssertTooDeep(result, "| a | b |");
            Assert.Equal(5002, result.Document.Paragraphs.Count);
            Assert.Equal("| a | b |", result.Document.Paragraphs[0].Text);
            Assert.Equal("| c | d |", result.Document.Paragraphs[5001].Text);
        }

        // The size the review named: Markdig's limit counts cells, so four thousand rows of four columns is over it as well.
        [Trait("Size", "Large")]
        [Fact]
        public void ATableOfFourThousandRowsAndFourColumnsIsOneFindingAndItsLinesAreText()
        {
            string text = "| a | b | c | d |\n| --- | --- | --- | --- |\n" + Repeat("| e | f | g | h |\n", 4000);
            var result = ParseSafely(text);
            AssertTooDeep(result, "| a | b | c | d |");
            Assert.Equal(4002, result.Document.Paragraphs.Count);
            Assert.Equal("| e | f | g | h |", result.Document.Paragraphs[4001].Text);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void AValidTableOfFourThousandRowsAndFourColumnsIsWrittenAndReadsBack()
        {
            var paragraphs = new Paragraph[4001];
            paragraphs[0] = B.P("GDR Table Header", B.En("a\tb\tc\td"));
            for (int i = 1; i < paragraphs.Length; i++) paragraphs[i] = B.P("GDR Table Row", B.En("e\tf\tg\th"));
            AssertWritesAndReadsBack(paragraphs);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void TenThousandEmphasesInOneParagraphAreOneFindingAndLiteralText()
        {
            string line = Repeat("*a* ", 10_000).TrimEnd();
            var result = ParseSafely(line);
            AssertTooDeep(result, line);
            Assert.Equal(line, AllText(result));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void ABlockThatIsTooBigDoesNotSpoilTheBlocksAroundIt()
        {
            string big = Repeat("*a* ", 10_000).TrimEnd();
            var result = ParseSafely("# Before\n\n" + big + "\n\n- after **bold**\n\n[x]{wss=1}");
            Assert.Equal(new[] { "Heading 1", "Normal", "Bulleted List", "Normal" }, result.Document.Paragraphs.Select(p => p.Style).ToArray());
            Assert.Equal("after bold", result.Document.Paragraphs[2].Text);
            Assert.Equal(new[] { MarkdownDiagnosticIds.TooDeep, "fwmd.syntax.unknown-attribute" }, result.Diagnostics.Select(d => d.Id).ToArray());
            Assert.Equal(3, result.Diagnostics[0].Line);
            Assert.Equal(big, result.Diagnostics[0].Observed);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void TenThousandEmphasesInAFenceLineAndInTheAbstractAreFindingsToo()
        {
            string line = Repeat("*a* ", 10_000).TrimEnd();
            var fence = ParseSafely("::: {custom-style=S}\n" + line + "\n:::");
            var finding = Assert.Single(fence.Diagnostics, d => d.Id == MarkdownDiagnosticIds.TooDeep);
            Assert.Equal(2, finding.Line);
            Assert.Equal(line, AllText(fence));
            var inline = OnSmallStack(() => MarkdownConverter.ParseInline(line));
            Assert.Single(inline.Diagnostics, d => d.Id == MarkdownDiagnosticIds.TooDeep);
            Assert.Equal(line, string.Concat(inline.Runs.Select(r => r.Text)));
        }

        // The writer reads back what it writes, so it meets the same limits on its own output. It always returns text that reads back.
        static void AssertWritesAndReadsBack(params Paragraph[] paragraphs)
        {
            var document = B.Doc(paragraphs);
            string markdown = OnSmallStack(() => MarkdownConverter.ToMarkdown(document, Hosts.Project()));
            var back = OnSmallStack(() => MarkdownConverter.Parse(markdown, Hosts.Project()));
            Assert.DoesNotContain(back.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void TextOfThreeThousandBracketsAroundASpanClosingIsWrittenAndReadsBack()
            => AssertWritesAndReadsBack(B.Plain(Repeat("[", 3000) + "x" + Repeat("]{.v}", 3000)));

        [Trait("Size", "Large")]
        [Fact]
        public void FiveThousandUnmatchedEmphasisOpenersAreWrittenAndReadBack()
            => AssertWritesAndReadsBack(B.Plain(Repeat("*a ", 5000) + "x"));

        [Trait("Size", "Large")]
        [Fact]
        public void AValidTableOfFiveThousandRowsIsWrittenAsAFenceAndReadsBack()
        {
            var paragraphs = new Paragraph[5001];
            paragraphs[0] = B.P("GDR Table Header", B.En("a\tb"));
            for (int i = 1; i < paragraphs.Length; i++) paragraphs[i] = B.P("GDR Table Row", B.En("c\td"));
            AssertWritesAndReadsBack(paragraphs);
        }

        [Trait("Size", "Large")]
        [Fact]
        public void TenThousandBoldRunsInOneParagraphAreWrittenAndReadBack()
        {
            var runs = new Run[10_000];
            for (int i = 0; i < runs.Length; i++) runs[i] = i % 2 == 0 ? B.BoldRun("en", "a" + i) : B.En(" b" + i + " ");
            AssertWritesAndReadsBack(B.P("Normal", runs));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void TwentyThousandRunsInOneParagraphAreWrittenAndReadBack()
        {
            var runs = new Run[20_000];
            for (int i = 0; i < runs.Length; i++) runs[i] = i % 4 == 0 ? B.BoldRun("en", "a" + i) : i % 4 == 1 ? B.R("ceb", "b" + i) : i % 4 == 2 ? B.ItalicRun("en", "c" + i) : B.En(" d" + i + " ");
            AssertWritesAndReadsBack(B.P("Normal", runs));
        }

        [Trait("Size", "Large")]
        [Fact]
        public void TwentyThousandRunsInOneParagraphWithoutMarksAreRead()
        {
            string text = Repeat("[a]{.v} b ", 10_000);
            var result = ParseSafely(text);
            Assert.False(result.HasErrors);
        }
    }
}
namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>The writer's own paths are linear: only Markdig's slow paths (a quadratic <c>[a](</c> run, deep lists, a table read) are left, and docs/reference/markdown.md says so.
    /// Counted, not timed (issue 141): the work is read from <see cref="WriterWork"/>, and four times the input must be about four times the work, not sixteen.</summary>
    public class WriterGrowthTests
    {
        /// <summary>The work the writer did on one document: the units its inline generator looked at, and the characters it read back to check them.</summary>
        static (long Steps, long Characters) WorkOf(Paragraph[] paragraphs)
        {
            WriterWork.Steps = 0;
            WriterWork.Characters = 0;
            MarkdownConverter.ToMarkdown(B.Doc(paragraphs), Hosts.Project());
            return (WriterWork.Steps, WriterWork.Characters);
        }

        [Fact]
        public void RepeatedAmpersandsAreWrittenInLinearTime()
        {
            var small = WorkOf(new[] { B.Plain(string.Concat(System.Linq.Enumerable.Repeat("&a", 8_000))) });
            var large = WorkOf(new[] { B.Plain(string.Concat(System.Linq.Enumerable.Repeat("&a", 32_000))) });
            Assert.True(small.Steps > 0);
            Assert.True(large.Steps < 8 * small.Steps, "steps: " + small.Steps + " for 8,000 ampersands, " + large.Steps + " for 32,000");   // four times the input: about four times the work, not sixteen
        }

        [Fact]
        public void FourThousandTableRowsAreWrittenInLinearTime()
        {
            // Above about three thousand rows Markdig cannot read the table back, so the writer writes it as divs; both sizes here take that path, and the
            // test is about the writer's own work on it, so the check is twice the rows and twice the work.
            var small = WorkOf(TableOfRows(4_000));
            var large = WorkOf(TableOfRows(8_000));
            Assert.True(small.Characters > 0);
            Assert.True(large.Steps < 3 * small.Steps, "steps: " + small.Steps + " for 4,000 rows, " + large.Steps + " for 8,000");   // twice the rows: about twice the work, not four times
            Assert.True(large.Characters < 3 * small.Characters, "characters read back: " + small.Characters + " for 4,000 rows, " + large.Characters + " for 8,000");
        }

        static Paragraph[] TableOfRows(int rows)
        {
            var paragraphs = new System.Collections.Generic.List<Paragraph> { B.P("GDR Table Header", B.En("a\tb")) };
            for (int i = 1; i < rows; i++) paragraphs.Add(B.P("GDR Table Row", B.En("c" + i + "\td")));
            return paragraphs.ToArray();
        }
    }
}
