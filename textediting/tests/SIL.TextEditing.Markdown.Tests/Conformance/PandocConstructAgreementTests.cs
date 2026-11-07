using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>The reader reports a line as a Pandoc construct exactly when Pandoc reads the text as something other than one paragraph (spec 3 and 7). Each line is tried
    /// as the whole text and as the second line of a wrapped paragraph; where Pandoc keeps one paragraph the reader must report nothing, and where Pandoc does not,
    /// the reader must either report it or read the same structure itself (a heading, a list).</summary>
    public class PandocConstructAgreementTests
    {
        static readonly string[] Lines =
        {
            "M. Dixon describes the pattern", "I. e. the form", "C. f. the form", "A. Consonants", "A.  Consonants", "I. Introduction", "V. the variant forms", "Mix. of both", "mix. of both",
            "MIX. of both", "Civil. The term", "II. x", "ii. x", "iv. x", "x. y", "a. palit", "b. c", "(i) x", "(A) x", "(1) x", "(a) x", "(@) x", "#. x", "#) x", "(#) x", "a) x", "A) x",
            "i) x", "I) x", "1) x", "10. x", "1990. That year", "2. Nouns", "Table: the forms", "table: the forms", "+nasal x", "+---+---+", "+-+-+", "#palit# is", "#x", "|a| is the phoneme", "| x",
            "|", "| |", ": marks length", ":: marks", "~ indicates", "[a]: the low vowel", "[a]: x", "[x] y", "x@y.org", "(1)", "a.", "- x", "-x", "* x", "> x", "# x", "[x]: y", "$5 and $10", "a~b", "a^b",
        };

        public static IEnumerable<object[]> Cases()
        {
            foreach (string line in Lines)
            {
                yield return new object[] { line + "\n" };
                yield return new object[] { "The data show this pattern\n" + line + "\n" };
            }
        }

        [PandocTheory]
        [MemberData(nameof(Cases))]
        public void TheReaderReportsWhatPandocReadsAsAnotherStructure(string markdown)
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            {
                var blocks = Json.Arr(Json.Obj(Json.Parse(PandocRunner.ToJson(markdown)))["blocks"]);
                bool onePara = blocks.Count == 1 && Json.Str(Json.Obj(blocks[0])["t"]) == "Para";
                var result = MarkdownConverter.Parse(markdown, new MarkdownOptions { WriteFrontMatter = false });
                bool reported = result.Diagnostics.Any(d => d.Id == "fwmd.syntax.unsupported-markup");
                bool sameStructure = result.Document.Paragraphs.Any(p => p.Style != "Normal") || result.Document.Paragraphs.Count > 1;
                string what = markdown.Replace("\n", "|");
                if (onePara) Assert.False(reported || result.HasErrors, "Pandoc reads one paragraph, but the reader reports: " + what);
                else Assert.True(reported || sameStructure || result.HasErrors, "Pandoc reads another structure and the reader does not notice: " + what);
            }
        }
    }
}
