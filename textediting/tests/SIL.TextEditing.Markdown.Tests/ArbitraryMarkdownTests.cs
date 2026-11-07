using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Markdown to model to Markdown is stable for any text at all, not only for text the writer made: whatever the parser makes
    /// of a text, writing it and reading it again gives the same model and the same text.</summary>
    public class ArbitraryMarkdownTests
    {
        static readonly string[] Pieces =
        {
            "word", "mang", "ng", " ", "  ", "\n", "\n\n", "\n\n\n", "# ", "## ", "#### ", "- ", "* ", "+ ", "1. ", "2) ", "> ", ">", "| a | b |\n| --- | --- |\n", "| c | d |", "|", "| :---: |",
            "::: {style=Example}\n", "::: {style=\"A b\" fontsize=12000}\n", ":::\n", ":::", "{empty}", "{empty style=\"Heading 2\"}", "{empty v}", "{empty", "{}", "{a,b}", "{v}",
            "**", "*", "_", "__", "***", "[", "]", "[x]", "](x)", "[x](y)", "![c](fw:picture/00000000-0000-0000-0000-000000000000)", "]{v}", "]{ws=ar}", "]{bold=on}", "]{cs=Strong}",
            "]{p3=\"1:3\"}", "]{s6=q}", "]{ws=", "[[w:x]]", "[[w:x]]{guid=1}", "&#x20;", "&#x9;", "&#xD800;", "&amp;", "&", "&#", "\\", "\\\\", "\\\n", "\\[", "\\*", "<b>", "<", "`", "``",
            "---", "===", "***\n", "    ", "\t", "\"", "=", "{", "}", "(", ")", "!", "~", "{bold=on}", "{ws=ceb}", "{style=x}", "<!-- c -->", "<http://a.b>",
        };

        public static IEnumerable<object[]> Seeds()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("TEXTEDITING_MARKDOWN_SEEDS"), out int configured) ? configured : 600;
            for (int seed = 1; seed <= count; seed++) yield return new object[] { seed };
        }

        static string Text(int seed)
        {
            var random = new Random(seed);
            var sb = new StringBuilder();
            int count = random.Next(1, 25);
            for (int i = 0; i < count; i++) sb.Append(Pieces[random.Next(Pieces.Length)]);
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void AnyTextIsStableAfterOneNormalisation(int seed)
        {
            string text = Text(seed);
            var options = Hosts.Project(new TestObjects());
            string once = string.Empty;
            try
            {
                var first = MarkdownConverter.Parse(text, options);
                once = MarkdownConverter.ToMarkdown(first.Document, options);
                var second = MarkdownConverter.Parse(once, options);
                Assert.DoesNotContain(second.Diagnostics, d => d.Severity == MarkdownSeverity.Error && d.Id != "fwmd.ws.unknown" && d.Id != "fwmd.link.not-found" && d.Id != "fwmd.picture.unknown");
                Assert.True(MarkdownConverter.AreEquivalent(first.Document.Paragraphs, second.Document.Paragraphs), "models differ");
                Assert.Equal(once, MarkdownConverter.ToMarkdown(second.Document, options));
            }
            catch (Exception e) when (!(e is Xunit.Sdk.XunitException))
            {
                throw new Xunit.Sdk.XunitException("seed " + seed + " text:\n" + text + "\n<end> once:\n" + once + "\n" + e);
            }
            catch (Xunit.Sdk.XunitException e)
            {
                throw new Xunit.Sdk.XunitException("seed " + seed + " text:\n" + text + "\n<end> once:\n" + once + "\n" + e.Message);
            }
        }
    }
}
