using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Tests.Conformance;
using SIL.TextEditing.Markdown.Writer;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>D8: the canonical form is defined by the spec and the fixtures, not by a parser. The writer reads back everything it writes and has a safer spelling to fall back to
    /// if the canonical one does not read back, so a test that only checks the round trip would pass even if the output depended on Markdig. These tests count the fallbacks:
    /// every fixture and every generated document must be written in the canonical spelling.</summary>
    public class CanonicalSpellingTests
    {
        static int Fallbacks => WriterFallbacks.Generic + WriterFallbacks.Safe + WriterFallbacks.Numeric + WriterFallbacks.SafeTable;

        static void Reset() { WriterFallbacks.Generic = 0; WriterFallbacks.Safe = 0; WriterFallbacks.Numeric = 0; WriterFallbacks.SafeTable = 0; }

        [Fact]
        public void EveryFixtureIsWrittenInTheCanonicalSpelling()
        {
            var gaps = new List<string>();
            foreach (var c in ConformanceLoader.All())
            {
                if (c.Inline || c.HasErrors) continue;
                var options = ConformanceTests.OptionsFor(c, out _);
                Document document;
                if (c.IsRead) document = MarkdownConverter.Parse(c.FullMarkdown(c.Markdown!), options).Document;
                else { document = new Document(); document.Paragraphs.AddRange(c.Paragraphs!.Select(p => p.Clone())); }
                Reset();
                MarkdownConverter.ToMarkdown(document, options);
                if (Fallbacks > 0) gaps.Add(c.Id + " (safe " + WriterFallbacks.Safe + ", numeric " + WriterFallbacks.Numeric + ", table " + WriterFallbacks.SafeTable + ")");
            }
            Assert.True(gaps.Count == 0, "These fixtures were not written in the canonical spelling, which is a gap in a rule:\n" + string.Join("\n", gaps));
        }

        [Fact]
        public void EveryGeneratedDocumentIsWrittenInTheCanonicalSpelling()
        {
            var gaps = new List<string>();
            int count = int.TryParse(System.Environment.GetEnvironmentVariable("TEXTEDITING_MARKDOWN_SEEDS"), out int configured) ? configured : 600;
            for (int seed = 1; seed <= count; seed++)
            {
                var objects = new TestObjects();
                var model = new ModelGenerator(seed, objects).NextDocument();
                Reset();
                MarkdownConverter.ToMarkdown(model, Hosts.Project(objects));
                if (Fallbacks > 0) gaps.Add("seed " + seed + " (safe " + WriterFallbacks.Safe + ", numeric " + WriterFallbacks.Numeric + ", table " + WriterFallbacks.SafeTable + ")");
            }
            Assert.True(gaps.Count == 0, "These documents were not written in the canonical spelling, which is a gap in a rule:\n" + string.Join("\n", gaps));
        }

        [Fact]
        public void TheHandWrittenTextsAreWrittenInTheCanonicalSpelling()
        {
            var gaps = new List<string>();
            foreach (object[] row in RoundTripTests.HandWritten())
            {
                var objects = new TestObjects();
                var options = Hosts.Project(objects);
                var parsed = MarkdownConverter.Parse((string)row[0], options);
                Reset();
                MarkdownConverter.ToMarkdown(parsed.Document, options);
                if (Fallbacks > 0) gaps.Add(((string)row[0]).Replace("\n", "\\n"));
            }
            Assert.True(gaps.Count == 0, string.Join("\n", gaps));
        }
    }
}
