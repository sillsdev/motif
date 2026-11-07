using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>What the host controls: the style names the notation maps to, which styles exist, the default writing systems, the handling of objects and the
    /// project's paths. The library knows no project.</summary>
    public class HostTests
    {
        const string EntryGuid = "11111111-2222-3333-4444-555555555555";

        sealed class Catalog : IStyleCatalog
        {
            public HashSet<string> Paragraph = new HashSet<string> { "Normal", "Heading 1", "Example" };
            public HashSet<string> Character = new HashSet<string> { "Strong", "Hyperlink" };
            public bool IsParagraphStyle(string name) => Paragraph.Contains(name);
            public bool IsCharacterStyle(string name) => Character.Contains(name);
        }

        static MarkdownOptions BodyOnly() => new MarkdownOptions { WriteFrontMatter = false };

        [Fact]
        public void TheStyleNamesTheNotationMapsToAreTheHosts()
        {
            var options = BodyOnly();
            options.StyleNames = new MarkdownStyleNames { Normal = "Standard", Heading1 = "Titre 1", BulletedList = "Liste", Hyperlink = "Lien" };
            var parsed = MarkdownConverter.Parse("plain\n\n# head\n\n- item\n\n[t](https://x.org)", options);
            Assert.Equal(new[] { "Standard", "Titre 1", "Liste", "Standard" }, parsed.Document.Paragraphs.Select(p => p.Style).ToArray());
            Assert.Equal("Lien", parsed.Document.Paragraphs[3].Runs.Single(r => r.ObjData != null).Props.StrProps[133]);
            Assert.Equal("plain\n\n# head\n\n- item\n\n[t](https://x.org)\n", MarkdownConverter.ToMarkdown(parsed.Document, options));
        }

        [Fact]
        public void AParagraphWithTheDefaultNameIsADivWhenTheHostRenamesTheStyle()
        {
            var options = BodyOnly();
            options.StyleNames = new MarkdownStyleNames { Normal = "Standard" };
            var document = B.Doc(B.P("Normal", B.En("x")));
            Assert.Equal("::: {custom-style=Normal}\nx\n:::\n", MarkdownConverter.ToMarkdown(document, options));
        }

        [Fact]
        public void AnUnknownStyleIsReportedByTheCatalog()
        {
            var options = BodyOnly();
            options.Styles = new Catalog();
            var result = MarkdownConverter.Parse("::: {custom-style=Exampel}\nx\n:::\n\n[y]{custom-style=Stong}\n\n[z]{custom-style=Strong}\n\n::: {custom-style=Example}\nok\n:::", options);
            Assert.Equal(new[] { "fwmd.style.unknown", "fwmd.style.unknown" }, result.Diagnostics.Select(d => d.Id).ToArray());
            Assert.Equal(1, result.Diagnostics[0].Line);
        }

        [Fact]
        public void TheDefaultsAreTheHostsWritingSystemsWhenThereIsNoFrontMatter()
        {
            var options = BodyOnly();
            options.WritingSystems = new FixedWritingSystems("fr", "mg");
            var parsed = MarkdownConverter.Parse("a [b]{.v} [c]{ws=en}", options);
            Assert.Equal(new[] { "fr", "mg", "fr", "en" }, parsed.Document.Paragraphs[0].Runs.Select(r => r.Ws).ToArray());
            Assert.Equal("a [b]{.v} [c]{ws=en}\n", MarkdownConverter.ToMarkdown(parsed.Document, options));
            Assert.Equal("en", new MarkdownOptions().WritingSystems.DefaultAnalysis);
            Assert.Equal("Normal", options.StyleNames.Normal);
            Assert.Same(DefaultObjectMapper.Instance, options.Objects);
        }

        [Fact]
        public void TheFrontMatterNamesTheDefaultsWhateverTheHostsAre()
        {
            var options = new MarkdownOptions { WritingSystems = new FixedWritingSystems("fr", "mg") };
            var parsed = MarkdownConverter.Parse("---\nfwmd: 1\nanalysis: \"en\"\nvernacular: \"ceb\"\n---\n\na [b]{.v}", options);
            Assert.Equal(new[] { "en", "ceb" }, parsed.Document.Paragraphs[0].Runs.Select(r => r.Ws).ToArray());
            Assert.Equal("en", parsed.DefaultAnalysis);
            Assert.Equal("ceb", parsed.FrontMatter!.Vernacular);
        }

        [Fact]
        public void AStyleNameThatMustBeThereRefusesNull()
        {
            var names = new MarkdownStyleNames();
            Assert.Throws<ArgumentNullException>(() => names.Normal = null!);
            Assert.Throws<ArgumentNullException>(() => names.Heading1 = null!);
            Assert.Throws<ArgumentNullException>(() => names.Heading2 = null!);
            Assert.Throws<ArgumentNullException>(() => names.Heading3 = null!);
            Assert.Throws<ArgumentNullException>(() => names.BulletedList = null!);
            Assert.Throws<ArgumentNullException>(() => names.NumberedList = null!);
            Assert.Throws<ArgumentNullException>(() => names.BlockQuote = null!);
            Assert.Throws<ArgumentNullException>(() => names.Hyperlink = null!);
            Assert.Equal("Heading 3", names.Heading3);
        }

        [Fact]
        public void WithNoTableStylesAPipeTableIsAFindingAndRowsAreWrittenAsDivs()
        {
            var options = BodyOnly();
            Assert.Null(options.StyleNames.TableHeader);
            var parsed = MarkdownConverter.Parse("| a |\n|---|\n| b |", options);
            Assert.Equal("fwmd.syntax.unsupported-markup", Assert.Single(parsed.Diagnostics).Id);
            Assert.Equal(new[] { "Normal", "Normal" }, parsed.Document.Paragraphs.Select(p => p.Style).ToArray());
            var document = B.Doc(B.P("Table Header", B.En("a")), B.P("Table Row", B.En("b")));
            Assert.DoesNotContain("|", MarkdownConverter.ToMarkdown(document, options));
        }

        [Fact]
        public void HeadingAttributesAreAFindingButABlockOfTheNotationAtTheEndIsNot()
        {
            Assert.Equal("fwmd.syntax.unsupported-markup", Assert.Single(MarkdownConverter.Parse("# x {#id .c}", BodyOnly()).Diagnostics).Id);
            Assert.Empty(MarkdownConverter.Parse("# x [y]{.v}", BodyOnly()).Diagnostics);
            Assert.DoesNotContain(MarkdownConverter.Parse("# [y]{s147=\"ng {ws=en}\"}", BodyOnly()).Diagnostics, d => d.Id == "fwmd.syntax.unsupported-markup");
        }

        [Fact]
        public void AnOptionThatMustBeThereRefusesNull()
        {
            var options = new MarkdownOptions();
            Assert.Throws<ArgumentNullException>(() => options.WritingSystems = null!);
            Assert.Throws<ArgumentNullException>(() => options.StyleNames = null!);
            Assert.Throws<ArgumentNullException>(() => options.Objects = null!);
            // The catalog and the path resolver are optional.
            options.Styles = null;
            options.Paths = null;
            Assert.Null(options.Styles);
            Assert.Null(options.Paths);
            // The options keep what they had when a null is refused.
            Assert.Equal("en", options.WritingSystems.DefaultAnalysis);
            Assert.True(options.WriteFrontMatter);
            Assert.Equal(MarkdownLinkForm.RootPath, options.LinkForm);
        }

        [Fact]
        public void WithoutAHostEveryObjectIsGenericExceptExternalPathsAndSilfwLinks()
        {
            var options = BodyOnly();
            var document = B.Doc(B.P("Normal",
                B.R("en", "￼", null, TestObjects.PictureData(new Guid("99999999-8888-7777-6666-555555555555"))),
                B.R("en", "see", p => p.SetStr(133, "Hyperlink"), TestObjects.LinkData("lexiconEdit", new Guid(EntryGuid))),
                B.R("en", "web", p => p.SetStr(133, "Hyperlink"), TestObjects.ExternalPath + "https://x.org")));
            string text = MarkdownConverter.ToMarkdown(document, options);
            Assert.StartsWith("[&#xFFFC;]{s6x=0008", text);
            Assert.Contains("[see](/objects/" + EntryGuid + "){tool=lexiconEdit}", text);
            Assert.Contains("[web](https://x.org)", text);
            var back = MarkdownConverter.Parse(text, options);
            Assert.False(back.HasErrors);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs));
        }

        [Fact]
        public void APathWithNoResolverNeedsAGuid()
        {
            var result = MarkdownConverter.Parse("[x](/lexicon/palit)", BodyOnly());
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.link.not-found", d.Id);
            // With a GUID the path is kept as written and nothing is asked of a project.
            var withGuid = MarkdownConverter.Parse("[x](/lexicon/palit){guid=" + EntryGuid + "}", BodyOnly());
            Assert.Empty(withGuid.Diagnostics);
            var obj = Assert.Single(withGuid.Objects);
            Assert.Equal(new Guid(EntryGuid), obj.Guid);
            Assert.Equal("lexiconEdit", obj.Tool);
        }

        [Fact]
        public void TheHostSeesTheDescriptionOfEachLink()
        {
            var seen = new List<MarkdownObject>();
            var options = BodyOnly();
            options.Objects = new Spy(seen);
            var document = B.Doc(B.P("Normal", B.R("en", "palit", p => p.SetStr(133, "Hyperlink"), "any")));
            string text = MarkdownConverter.ToMarkdown(document, options);
            Assert.Equal("[palit](/objects/" + EntryGuid + "){.name tool=oddEdit}\n", text);
            Assert.Equal("palit", seen.Single().Name);
        }

        sealed class Spy : IObjectMapper
        {
            readonly List<MarkdownObject> _seen;
            public Spy(List<MarkdownObject> seen) { _seen = seen; }

            public MarkdownObject? Describe(string objData)
            {
                var link = new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, Name = "palit", Guid = new Guid(EntryGuid), Tool = "oddEdit" };
                _seen.Add(link);
                return link;
            }

            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
            {
                objData = "any";
                problem = null;
                return true;
            }
        }

        [Fact]
        public void ADiagnosticFromTheHostIsPlacedAtTheMarkup()
        {
            var options = BodyOnly();
            options.Objects = new Refusing();
            var result = MarkdownConverter.Parse("a\n\nsee [palit](/objects/" + EntryGuid + ") here", options);
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.link.not-found", d.Id);
            Assert.Equal(3, d.Line);
            Assert.Equal(5, d.Column);
            Assert.Equal("[palit](/objects/" + EntryGuid + ")", d.Observed);
            Assert.Contains("no entry palit", d.Message);
        }

        sealed class Refusing : IObjectMapper
        {
            public MarkdownObject? Describe(string objData) => null;
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
            {
                objData = string.Empty;
                problem = "no entry palit";
                return false;
            }
        }

        [Fact]
        public void AnObjectResolversWarningComesWithTheDataAndIsPlacedAtTheMarkup()
        {
            var options = BodyOnly();
            options.Objects = new Warning();
            var result = MarkdownConverter.Parse("[x](/objects/" + EntryGuid + ")", options);
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal(MarkdownSeverity.Warning, d.Severity);
            Assert.Equal("fwmd.link.broken", d.Id);
            Assert.Equal("data", result.Document.Paragraphs[0].Runs[0].ObjData);
        }

        sealed class Warning : IObjectMapper, IObjectResolver
        {
            public MarkdownObject? Describe(string objData) => null;
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem) { objData = "data"; problem = null; return true; }
            public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
            {
                objData = "data";
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "The link target was deleted; the text is kept.");
                return true;
            }
        }
    }
}
