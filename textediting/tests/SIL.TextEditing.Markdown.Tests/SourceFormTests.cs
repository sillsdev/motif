using System;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>FieldWorks Markdown as a field's source form (ADR 0035): what the field shows, what reading it back gives, and where the findings and muted
    /// GUIDs land in the language service's terms.</summary>
    public class SourceFormTests
    {
        const string EntryGuid = "11111111-2222-3333-4444-555555555555";

        static FieldWorksMarkdownSourceForm Form(IObjectMapper? objects = null)
        {
            var options = new MarkdownOptions { WritingSystems = new FixedWritingSystems("en", "en") };
            if (objects != null) options.Objects = objects;
            return new FieldWorksMarkdownSourceForm(options);
        }

        [Fact]
        public void Several_paragraphs_are_written_without_front_matter_and_read_back_the_same()
        {
            var form = Form();
            var source = ClipboardFormTests.Fragment();
            string text = Assert.IsType<string>(form.Write(source, false));
            Assert.DoesNotContain("fwmd:", text);
            Assert.False(text.EndsWith("\n", StringComparison.Ordinal));
            Assert.True(form.Options.WriteFrontMatter);   // the host's options are not changed
            var read = form.Read(text, false);
            Assert.False(read.HasErrors);
            Assert.Equal(source.Length, read.Paragraphs.Count);
            Assert.All(source.Zip(read.Paragraphs, (a, b) => (a, b)), pair => Assert.True(form.AreEquivalent(pair.a, pair.b)));
            Assert.Equal("Markdown", form.Name);
        }

        [Fact]
        public void One_paragraph_uses_the_inline_grammar_so_block_syntax_is_a_finding()
        {
            var form = Form();
            var p = new Paragraph { Style = "Heading 1", DefaultWs = "en" };
            var strong = new Run { Ws = "en", Text = "bold" };
            strong.Props.SetStr(TextPropertyNumbers.NamedStyle, "Strong");
            p.Runs.Add(new Run { Ws = "en", Text = "a " });
            p.Runs.Add(strong);
            string text = Assert.IsType<string>(form.Write(new[] { p }, true));
            Assert.DoesNotContain("#", text);   // no paragraph syntax: the field keeps its own style
            var read = form.Read(text, true);
            Assert.False(read.HasErrors);
            Assert.True(form.AreEquivalent(new Paragraph { Style = p.Style, DefaultWs = "en", Runs = { p.Runs[0], p.Runs[1] } },
                new Paragraph { Style = p.Style, DefaultWs = "en", Runs = { read.Paragraphs.Single().Runs[0], read.Paragraphs.Single().Runs[1] } }));
            var block = form.Read("# heading", true);
            Assert.Contains(block.Diagnostics, d => d.Code == MarkdownDiagnosticIds.FieldBlockSyntax);
        }

        [Fact]
        public void An_error_is_an_error_diagnostic_on_its_line_and_blocks_the_return()
        {
            var read = Form().Read("first\n\nsee [x](/lexicon/palit) here", false);
            Assert.True(read.HasErrors);
            var d = Assert.Single(read.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal("fwmd.link.not-found", d.Code);
            Assert.Equal("fwmd", d.Source);
            Assert.Equal(new LanguagePosition(2, 4), d.Range.Start);
            Assert.Equal(new LanguagePosition(2, 4 + "[x](/lexicon/palit)".Length), d.Range.End);
        }

        sealed class WarningHost : IObjectMapper, IObjectResolver
        {
            public MarkdownObject? Describe(string objData) => null;
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem) => throw new InvalidOperationException();
            public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
            {
                objData = TestObjects.LinkData("lexiconEdit", new Guid(EntryGuid));
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "Renamed.") { Replacement = "[palit](/lexicon/palit2)" };
                return true;
            }
        }

        [Fact]
        public void A_warning_does_not_block_and_its_replacement_is_a_fix()
        {
            var read = Form(new WarningHost()).Read("[palit](/objects/" + EntryGuid + ")", false);
            Assert.False(read.HasErrors, string.Join("; ", read.Diagnostics.Select(x => x.Code + " " + x.Message)));
            var d = Assert.Single(read.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            var fix = Assert.IsType<LanguageFix>(d.Fix);
            Assert.Equal("[palit](/lexicon/palit2)", Assert.Single(fix.Edits).NewText);
            Assert.Equal(d.Range, fix.Edits[0].Range);
        }

        [Fact]
        public void A_link_s_attribute_block_with_a_guid_is_a_muted_token_and_one_without_is_not()
        {
            string link = "[x](/lexicon/palit){guid=" + EntryGuid + "}";
            var read = Form().Read("a\n\nsee " + link, false);
            Assert.False(read.HasErrors);
            var token = Assert.Single(read.Tokens);
            Assert.Equal(SourceTokenKinds.Muted, token.Kind);
            int brace = "see ".Length + link.IndexOf('{');
            Assert.Equal(new LanguagePosition(2, brace), token.Range.Start);
            Assert.Equal(new LanguagePosition(2, "see ".Length + link.Length), token.Range.End);
            Assert.Empty(Form().Read("[web](https://x.org)", false).Tokens);
        }
    }
}
