using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>The core guarantee (spec 5): model to Markdown to model is lossless, and Markdown to model to Markdown is stable.
    /// Random documents from <see cref="ModelGenerator"/>; a failure prints its seed and the text.</summary>
    public class RoundTripTests
    {
        public static IEnumerable<object[]> Seeds()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("TEXTEDITING_MARKDOWN_SEEDS"), out int configured) ? configured : 600;
            for (int seed = 1; seed <= count; seed++) yield return new object[] { seed };
        }

        static string Describe(int seed, Document model, string? markdown)
        {
            var sb = new StringBuilder();
            sb.Append("seed ").Append(seed).Append('\n');
            sb.Append("model:\n").Append(ModelDump.Of(model)).Append('\n');
            if (markdown != null) sb.Append("markdown:\n").Append(markdown.Replace("\u0000", "\\0")).Append("<end>\n");
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void AModelSurvivesMarkdownUnchanged(int seed)
        {
            var objects = new TestObjects();
            var model = new ModelGenerator(seed, objects).NextDocument();
            var options = Hosts.Project(objects);
            string markdown;
            try { markdown = MarkdownConverter.ToMarkdown(model, options); }
            catch (Exception e) { throw new Xunit.Sdk.XunitException(Describe(seed, model, null) + e); }

            var parsed = MarkdownConverter.Parse(markdown, options);
            var errors = parsed.Diagnostics.Where(d => d.Severity == MarkdownSeverity.Error).ToList();
            Assert.True(errors.Count == 0, Describe(seed, model, markdown) + string.Join("\n", errors));
            Assert.True(MarkdownConverter.AreEquivalent(model.Paragraphs, parsed.Document.Paragraphs),
                Describe(seed, model, markdown) + "parsed:\n" + ModelDump.Of(parsed.Document));
            string again = MarkdownConverter.ToMarkdown(parsed.Document, options);
            Assert.True(again == markdown, Describe(seed, model, markdown) + "written again:\n" + again);
        }

        /// <summary>Texts a person or an agent might write by hand. Each one is normalised once and then stays put.</summary>
        public static IEnumerable<object[]> HandWritten()
        {
            foreach (var text in new[]
            {
                "Plain paragraph.",
                "# Heading\n\nText with **bold**, *italic* and ***both***.",
                "- one\n- two\n  continues\n\n1. a\n2. b",
                "> quoted\n>\n> second",
                "| A | B |\n|---|---|\n| 1 | 2 |",
                "| A | B |\n| --- | --- |\n| x | |",
                "::: {custom-style=Example}\n\\# not a heading\n\n\\- not a list\n:::",
                "::: {custom-style=Example fontsize=12000 align=center}\nx\n\ny\n:::",
                "[]{.empty}\n\n::: {custom-style=\"Heading 2\"}\n[]{.empty}\n:::",
                "[mang-]{.v} and [kaon]{ws=ceb-x-old} and [x]{.v custom-style=\"Emphasized Text\"}",
                "**[mang-]{.v}** and [**mang**]{.v bold=off}",
                "[palit](/objects/11111111-2222-3333-4444-555555555555) and [see this rule](/objects/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)",
                "[text](https://example.org){ws=ceb bold=on}",
                "![caption](/objects/99999999-8888-7777-6666-555555555555)",
                "[&#xFFFC;]{s6x=0001D80000610062}",
                "a\\\nb\\\nc",
                "  leading and   inner   spaces  ",
                "[ng] [+nasal] {a,b} C -> [+nasal] / _V /a/",
                "\\[ng]{.v} \\<b> \\`x\\` \\&amp; \\# \\- \\> 1\\. \\{empty} \\:::",
                "a * b * c and *d and e*",
                "_italic_ __bold__ a_b_c",
                "[x]{fontsize=14000 fontsizeUnit=rel MarginTop=-5 p999=3 s2=v s999=w}",
                "[x]{marginTop=100 forecolor=#FF0000 backcolor=transparent}",
                "[x]{p3=1:3 p2=1}",
                "Mixed [**bold [nested]{.v}**]{ws=ar} text",
                "[a [b]{.v} c]{ws=ar}",
                "trailing backslash \\\\",
                "tab&#x9;inside and nbsp&#xA0;and em&#x2003;space",
                "line one\n\nline two\n\n\n\nline three",
                "* star list\n* another",
                "Setext heading\n===\n\nand\n---",
                "x^2^ and H~2~O and $5 and $6 and a@b and @c",
                "Table: not a caption\n\n: not a definition\n\n(a) not a list\n\niv. not a list",
            })
                yield return new object[] { text };
        }

        [Theory]
        [MemberData(nameof(HandWritten))]
        public void MarkdownIsStableAfterOneNormalisation(string markdown)
        {
            var objects = new TestObjects();
            objects.Names[new Guid("11111111-2222-3333-4444-555555555555")] = "palit";
            objects.Names[new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")] = "Nasal";
            objects.Pictures.Add(new Guid("99999999-8888-7777-6666-555555555555"));
            var options = Hosts.Project(objects);
            var first = MarkdownConverter.Parse(markdown, options);
            string once = MarkdownConverter.ToMarkdown(first.Document, options);
            var second = MarkdownConverter.Parse(once, options);
            Assert.DoesNotContain(second.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.True(MarkdownConverter.AreEquivalent(first.Document.Paragraphs, second.Document.Paragraphs), once);
            string twice = MarkdownConverter.ToMarkdown(second.Document, options);
            Assert.Equal(once, twice);
        }

        [Theory]
        [MemberData(nameof(HandWritten))]
        public void WindowsLineEndingsReadTheSame(string markdown)
        {
            var options = Hosts.Project(new TestObjects());
            var lf = MarkdownConverter.Parse(markdown, options);
            var crlf = MarkdownConverter.Parse(markdown.Replace("\n", "\r\n"), options);
            Assert.True(MarkdownConverter.AreEquivalent(lf.Document.Paragraphs, crlf.Document.Paragraphs), ModelDump.Of(lf.Document) + "\n--\n" + ModelDump.Of(crlf.Document));
            Assert.Equal(lf.Diagnostics.Select(d => d.Id), crlf.Diagnostics.Select(d => d.Id));
        }

        [Fact]
        public void TheCanonicalFormIsIdempotentOnItsOwn()
        {
            var options = Hosts.Project();
            foreach (object[] row in HandWritten())
            {
                string once = MarkdownConverter.Canonicalize((string)row[0], options);
                Assert.Equal(once, MarkdownConverter.Canonicalize(once, options));
            }
        }

        [Fact]
        public void AnAbstractIsAnInlineOnlyTextThatRoundTrips()
        {
            var runs = new List<Run>
            {
                B.En("plain [ng] "), B.BoldRun("en", "bold"), B.En(" and "), B.R("ceb", "mang-"), B.En(" # - > not blocks\u2028second line"),
            };
            string text = MarkdownConverter.ToInlineMarkdown(runs, Hosts.Project());
            var back = MarkdownConverter.ParseInline(text, Hosts.Project());
            Assert.False(back.HasErrors);
            Assert.True(MarkdownConverter.AreEquivalent(new[] { B.P("Normal", runs.ToArray()) }, new[] { B.P("Normal", back.Runs.ToArray()) }), text);
            Assert.DoesNotContain("\n\n", text);
        }

        [Fact]
        public void BlockSyntaxInAnAbstractIsReported()
        {
            foreach (var text in new[] { "# heading", "- item", "> quote", "| a |\n| --- |" })
            {
                var result = MarkdownConverter.ParseInline(text, Hosts.Project());
                Assert.Contains(result.Diagnostics, d => d.Id == "fwmd.field.block-syntax");
            }
        }
    }
}
