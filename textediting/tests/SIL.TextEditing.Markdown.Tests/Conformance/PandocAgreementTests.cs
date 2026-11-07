using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Pandoc reads every conformance fixture the way this library does (ADR 0028, spec 6.4): the Markdown of each case, and the canonical Markdown the
    /// writer produces, is read with <c>pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t json</c>, the result is mapped to the run
    /// model (<see cref="PandocModel"/>), and the paragraphs must equal the ones the fixture expects. A fixture sets <c>"pandoc": false</c> (with a reason) where
    /// Pandoc is not expected to agree: an error case, or text that is not a spelling Pandoc is asked to read.</summary>
    public class PandocAgreementTests
    {
        public static IEnumerable<object[]> Cases()
            => ConformanceLoader.All().Where(c => c.Pandoc && !c.HasErrors).Select(c => new object[] { c.Id });

        [PandocTheory]
        [MemberData(nameof(Cases))]
        public void PandocReadsTheFixtureAsWeDo(string id)
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            var c = ConformanceLoader.Find(id);
            var options = ConformanceTests.OptionsFor(c, out _);
            options.WriteFrontMatter = false;
            // What the library itself reads is the reference when the fixture gives no paragraphs.
            var expected = c.Paragraphs;
            if (c.IsRead)
            {
                string full = c.FullMarkdown(c.Markdown!);
                if (expected == null) expected = MarkdownConverter.Parse(full, options).Document.Paragraphs.ToList();
                Compare(full, expected, options, "the Markdown of the case");
                if (c.Canonical != null) Compare(c.FullMarkdown(c.Canonical), expected, options, "its canonical form");
            }
            else Compare(c.FullMarkdown(c.Canonical!), expected!, options, "the canonical form of the model");
        }

        static void Compare(string markdown, IReadOnlyList<Paragraph> expected, MarkdownOptions options, string what)
        {
            List<Paragraph> actual;
            try { actual = PandocModel.Convert(PandocRunner.ToJson(markdown), options); }
            catch (PandocMismatch e) { throw new Xunit.Sdk.XunitException("Pandoc reads " + what + " as something the profile has no meaning for: " + e.Message + "\n" + markdown); }
            ConformanceTests.AssertParagraphs(expected, actual, "Pandoc, reading " + what + ":\n" + markdown);
        }

        [PandocFact]
        public void ThePinnedPandocIsTheOneTheWorkflowInstalls()
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            // The workflow pins 3.x; the AST of an older Pandoc differs (table cells, attributes), so say which one this run used.
            Assert.StartsWith("pandoc 3.", PandocRunner.Version);
            // CI says which version it installed (PANDOC_VERSION), and the run must have used exactly that one.
            string? pinned = Environment.GetEnvironmentVariable("PANDOC_VERSION");
            if (!string.IsNullOrEmpty(pinned)) Assert.Equal("pandoc " + pinned, PandocRunner.Version);
        }
    }
}
namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>The documents the round-trip tests generate (every property number, several writing systems, object runs, NFD, surrogates, NUL, and the linguistic stress
    /// characters of spec 6.5) are read by Pandoc too: what it reads must be the model that was written. The number of seeds is
    /// <c>TEXTEDITING_PANDOC_SEEDS</c> (default 150; every seed starts a Pandoc process).</summary>
    public class PandocGeneratedDocumentTests
    {
        public static IEnumerable<object[]> Seeds()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("TEXTEDITING_PANDOC_SEEDS"), out int configured) ? configured : 150;
            for (int seed = 1; seed <= count; seed++) yield return new object[] { seed };
        }

        [PandocTheory]
        [MemberData(nameof(Seeds))]
        public void PandocReadsAGeneratedDocumentAsWeWroteIt(int seed)
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            var objects = new TestObjects();
            var model = new ModelGenerator(seed, objects).NextDocument();
            var options = Hosts.Project(objects);
            string markdown = MarkdownConverter.ToMarkdown(model, options);
            List<Paragraph> actual;
            try { actual = PandocModel.Convert(PandocRunner.ToJson(markdown), options); }
            catch (PandocMismatch e) { throw new Xunit.Sdk.XunitException("seed " + seed + ": Pandoc reads the text as something the profile has no meaning for: " + e.Message + "\n" + markdown); }
            ConformanceTests.AssertParagraphs(model.Paragraphs, actual, "seed " + seed + ", Pandoc reading:\n" + markdown);
        }
    }
}
namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>The brief: Pandoc reads every fixture. A case with an error finding is read as the text it is only by this library, so it is excluded by its nature; any other case
    /// that Pandoc is not asked to read says why.</summary>
    public class PandocExclusionTests
    {
        [Fact]
        public void EveryCaseWithoutAnErrorIsReadByPandocOrSaysWhyNot()
        {
            var unexplained = ConformanceLoader.All().Where(c => !c.Pandoc && !c.HasErrors && c.PandocReason == null).Select(c => c.Id).ToList();
            Assert.True(unexplained.Count == 0, "These cases without an error switch Pandoc off with no reason (\"pandoc\": \"why\"):\n" + string.Join("\n", unexplained));
        }

        [Fact]
        public void TheExcludedCasesAreCounted()
        {
            var all = ConformanceLoader.All();
            int errors = all.Count(c => c.HasErrors);
            int excluded = all.Count(c => !c.Pandoc && !c.HasErrors);
            // A number to keep an eye on: every case here is one the format is not checked against Pandoc for.
            Assert.True(excluded <= 12, excluded + " cases without an error are not read by Pandoc (" + errors + " have errors)");
        }
    }
}
