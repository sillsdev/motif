using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Runs the JSON conformance fixtures in tests/conformance (README.md there): Markdown in, expected paragraphs and findings out, and the model to
    /// the canonical Markdown. The fixtures and the spec define the format; this library has to pass every one.</summary>
    public class ConformanceTests
    {
        public static IEnumerable<object[]> Cases() => ConformanceLoader.All().Select(c => new object[] { c.Id });

        [Theory]
        [MemberData(nameof(Cases))]
        public void FixtureCase(string id) => Run(ConformanceLoader.Find(id));

        /// <summary>The ids the library reports that a fixture can express without a host's help, each with a case that expects it and a case that reads the
        /// construct correctly (no finding of that id).</summary>
        static readonly string[] CheckedIds =
        {
            "fwmd.version.unsupported", "fwmd.syntax.unclosed-mark", "fwmd.syntax.unknown-attribute", "fwmd.syntax.duplicate-attribute", "fwmd.syntax.malformed-attribute",
            "fwmd.syntax.too-deep", "fwmd.syntax.unsupported-markup", "fwmd.ws.unknown", "fwmd.link.not-found", "fwmd.link.ambiguous", "fwmd.link.broken",
            "fwmd.link.guid-mismatch", "fwmd.link.relative-without-base", "fwmd.link.stale-path", "fwmd.link.rebound", "fwmd.link.url-rebuilt", "fwmd.style.unknown",
            "fwmd.heading.too-deep", "fwmd.table.ragged", "fwmd.table.tab-in-cell", "fwmd.table.alignment", "fwmd.picture.unknown", "fwmd.field.block-syntax", "fwmd.prop.unknown-number",
            "fwmd.record.record-file",
        };

        [Fact]
        public void EveryPublicFindingIdIsGuardedOrNamedAsHostOnly()
        {
            // fwmd.ws.unmarked-vernacular is a constant for hosts to report; the reader never emits it, so no fixture can expect it.
            var hostOnly = new[] { MarkdownDiagnosticIds.WsUnmarkedVernacular };
            var unguarded = typeof(MarkdownDiagnosticIds).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).Where(v => !CheckedIds.Contains(v) && !hostOnly.Contains(v)).ToList();
            Assert.True(unguarded.Count == 0, "Add a positive and a negative fixture case, and the id to CheckedIds: " + string.Join(", ", unguarded));
        }

        [Fact]
        public void EveryFindingIdHasAPositiveAndANegativeCase()
        {
            // The ids the library reports that a fixture can express without a host's help, each with a case that expects it and a case that reads the
            // construct correctly (no finding of that id).
            var ids = CheckedIds;
            var all = ConformanceLoader.All();
            var missing = new List<string>();
            foreach (string id in ids)
            {
                bool positive = all.Any(c => c.IsRead && c.Findings.Any(f => f.Id == id));
                bool negative = all.Any(c => c.IsRead && !c.Findings.Any(f => f.Id == id) && c.Name.StartsWith("ok: " + id, StringComparison.Ordinal));
                if (!positive || !negative) missing.Add(id + (positive ? string.Empty : " (no positive case)") + (negative ? string.Empty : " (no negative case, named \"ok: " + id + " ...\")"));
            }
            Assert.True(missing.Count == 0, string.Join("\n", missing));
        }

        // ---------------------------------------------------------------- the runner

        internal static MarkdownOptions OptionsFor(ConformanceCase c, out FixtureHost host)
        {
            host = FixtureHost.From(c.Project);
            var options = new MarkdownOptions
            {
                WritingSystems = host,
                Objects = host,
                Paths = host,
                Styles = host.HasStyles ? host : null,
                WriteFrontMatter = c.ExplicitFrontMatter,
            };
            if (host.TableHeaderStyle != null) options.StyleNames.TableHeader = host.TableHeaderStyle;
            if (host.TableRowStyle != null) options.StyleNames.TableRow = host.TableRowStyle;
            if (c.Options != null)
            {
                string? form = Json.OptStr(c.Options, "linkForm");
                if (form != null) options.LinkForm = form == "B" ? MarkdownLinkForm.FileRelative : form == "C" ? MarkdownLinkForm.SilfwUrl : form == "D" ? MarkdownLinkForm.MotifUrl : form == "E" ? MarkdownLinkForm.ObjectAlias : MarkdownLinkForm.RootPath;
                options.LinkBase = Json.OptStr(c.Options, "linkBase");
                options.ProjectName = Json.OptStr(c.Options, "projectName");
                options.NormalizeText = Json.OptBool(c.Options, "normalize", true);
                if (Json.OptStr(c.Options, "unknownGuids") == "rebind") options.UnknownGuids = UnknownGuidPolicy.RebindByPath;
            }
            return options;
        }

        static void Run(ConformanceCase c)
        {
            var options = OptionsFor(c, out _);
            if (c.IsRead) RunRead(c, options);
            else RunWrite(c, options);
        }

        /// <summary>A multi-string field (spec 5.1): inline grammar only, one paragraph's runs, no front matter. The paragraph of the fixture holds the runs.</summary>
        static void RunInline(ConformanceCase c, MarkdownOptions options)
        {
            var result = MarkdownConverter.ParseInline(c.Markdown!, options);
            AssertFindings(c, result.Diagnostics);
            if (c.Paragraphs != null)
            {
                var read = new Paragraph { Style = c.Paragraphs[0].Style };
                foreach (var run in result.Runs) read.Runs.Add(run.Clone());
                read.DefaultWs = read.Runs.Count > 0 ? read.Runs[0].Ws : c.Paragraphs[0].DefaultWs;
                var expected = c.Paragraphs[0].Clone();
                expected.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                read.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                AssertParagraphs(new[] { c.Paragraphs[0] }, new[] { read }, "ParseInline");
            }
            if (c.CanonicalSpecified && !result.HasErrors)
            {
                string written = MarkdownConverter.ToInlineMarkdown(result.Runs, options);
                Assert.Equal(c.Canonical ?? c.Markdown!, written);
            }
        }

        static void RunRead(ConformanceCase c, MarkdownOptions options)
        {
            if (c.Inline) { RunInline(c, options); return; }
            string full = c.FullMarkdown(c.Markdown!);
            var result = MarkdownConverter.Parse(full, options);
            AssertFindings(c, result.Diagnostics);
            if (c.Paragraphs != null) AssertParagraphs(c.Paragraphs, result.Document.Paragraphs, "Parse");
            if (c.CanonicalSpecified && !result.HasErrors)
            {
                string written = MarkdownConverter.ToMarkdown(result.Document, options);
                string expected = c.Canonical ?? c.Markdown!;
                Assert.Equal(expected, written);
                // Canonical is idempotent: writing what was written changes nothing.
                var again = MarkdownConverter.Parse(c.FullMarkdown(written), options);
                Assert.Equal(written, MarkdownConverter.ToMarkdown(again.Document, options));
            }
        }

        static void AssertFindings(ConformanceCase c, IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            int shift = c.ExplicitFrontMatter ? 0 : ConformanceCase.StandardFrontMatterLines;
            var actual = diagnostics.Select(d => new ExpectedFinding
            {
                Id = d.Id,
                Severity = d.Severity == MarkdownSeverity.Error ? "error" : d.Severity == MarkdownSeverity.Warning ? "warning" : "suggestion",
                Line = d.Line - shift, Column = d.Column, EndLine = d.EndLine - shift, EndColumn = d.EndColumn,
            }).ToList();
            var expectedText = c.Findings.Select(f => f.ToString()).ToList();
            var actualText = actual.Select(f => f.ToString()).ToList();
            Assert.True(expectedText.SequenceEqual(actualText), "Findings differ.\nExpected:\n" + string.Join("\n", expectedText) + "\nActual:\n" + string.Join("\n", actualText) + "\nMessages:\n" + string.Join("\n", diagnostics.Select(d => d.Id + " [" + d.Severity + "] " + d.Message)));
            for (int i = 0; i < c.Findings.Count; i++)
            {
                var e = c.Findings[i];
                var d = diagnostics[i];
                if (e.HasFix) Assert.Equal(e.Fix, d.Replacement);
                if (e.Choices != null) Assert.Equal(e.Choices, d.Choices.ToList());
                if (e.Nearest != null) Assert.Equal(e.Nearest, d.Nearest.ToList());
            }
        }

        static void RunWrite(ConformanceCase c, MarkdownOptions options)
        {
            Assert.NotNull(c.Paragraphs);
            Assert.True(c.CanonicalSpecified && c.Canonical != null, "A case with no markdown needs a canonical string.");
            var document = new Document();
            document.Paragraphs.AddRange(c.Paragraphs!.Select(p => p.Clone()));
            string written = MarkdownConverter.ToMarkdown(document, options);
            Assert.Equal(c.Canonical, written);
            var back = MarkdownConverter.Parse(c.FullMarkdown(written), options);
            Assert.False(back.HasErrors, "The canonical text has errors: " + string.Join("; ", back.Diagnostics.Select(d => d.ToString())));
            AssertParagraphs(c.Paragraphs!, back.Document.Paragraphs, "Reading the canonical text");
        }

        internal static void AssertParagraphs(IReadOnlyList<Paragraph> expected, IReadOnlyList<Paragraph> actual, string what)
        {
            string e = string.Join("\n", expected.Select(ModelDump.Of)), a = string.Join("\n", actual.Select(ModelDump.Of));
            if (expected.Count != actual.Count) Assert.Fail(what + ": " + expected.Count + " paragraphs expected, " + actual.Count + " read.\nExpected:\n" + e + "\nActual:\n" + a);
            for (int i = 0; i < expected.Count; i++)
                if (!MarkdownConverter.AreEquivalent(expected[i], actual[i]))
                    Assert.Fail(what + ": paragraph " + (i + 1) + " differs.\nExpected: " + ModelDump.Of(expected[i]) + "\nActual:   " + ModelDump.Of(actual[i]));
        }
    }
}
namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Spec 4.4: one link, five spellings. The reader takes every spelling in any context and the writer writes the one asked for, so a link converts from
    /// any form to any other without loss.</summary>
    public class LinkFormConversionTests
    {
        static readonly MarkdownLinkForm[] Forms =
            { MarkdownLinkForm.RootPath, MarkdownLinkForm.FileRelative, MarkdownLinkForm.SilfwUrl, MarkdownLinkForm.MotifUrl, MarkdownLinkForm.ObjectAlias };

        static MarkdownOptions Options(MarkdownLinkForm form)
        {
            var c = new ConformanceCase { Project = (Dictionary<string, object?>)Json.Obj(Json.Parse(ConformanceLoader.ReadText(System.IO.Path.Combine(ConformanceLoader.Directory, "projects.json"))))["standard"]! };
            var options = ConformanceTests.OptionsFor(c, out _);
            options.WriteFrontMatter = false;
            options.LinkForm = form;
            options.LinkBase = "/lists/GDR/GDR-47.fw.md";
            options.ProjectName = "Sena3";
            return options;
        }

        const string Text =
            "See [palit](/lexicon/palit(2)){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}, [this rule](</grammar/phonological-rules/Nasal assimilation>){guid=0b1e2f3a-4c5d-4e6f-8a9b-0c1d2e3f4a5b}, "
            + "[(1)](/texts/Kuwento/3.2){.example guid=5b6c0e2a-1d3f-4c8e-9a71-0f2b3c4d5e6f}, [x](/lexicon/palit(2)#definition.en){guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}, "
            + "[gone](/objects/deadbeef-0000-4000-8000-000000000005){.name .broken}, [there](/objects/9c9c9c9c-1111-4111-8111-9c9c9c9c9c9c){url=\"silfw://localhost/link?database%3dOther%26tool%3dlexiconEdit%26guid%3d9c9c9c9c-1111-4111-8111-9c9c9c9c9c9c%26tag%3d\"} "
            + "and ![A hut](/files/pictures/hut.jpg){guid=1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9} and [web](https://example.org/a)\n";

        [Fact]
        public void ALinkConvertsFromAnyFormToAnyOther()
        {
            var model = MarkdownConverter.Parse(Text, Options(MarkdownLinkForm.RootPath));
            Assert.DoesNotContain(model.Diagnostics, d => d.Severity == MarkdownSeverity.Error);
            Assert.Equal(8, model.Objects.Count); // palit, the rule, the example, the field link, the deleted one, the other project's, the picture and the web address
            foreach (var from in Forms)
            {
                string a = MarkdownConverter.ToMarkdown(model.Document, Options(from));
                var pa = MarkdownConverter.Parse(a, Options(from));
                Assert.True(!pa.HasErrors, from + ":\n" + a + "\n" + string.Join("\n", pa.Diagnostics));
                ConformanceTests.AssertParagraphs(model.Document.Paragraphs, pa.Document.Paragraphs, "Reading form " + from + ":\n" + a);
                foreach (var to in Forms)
                {
                    string b = MarkdownConverter.ToMarkdown(pa.Document, Options(to));
                    var pb = MarkdownConverter.Parse(b, Options(to));
                    Assert.True(!pb.HasErrors, from + " to " + to + ":\n" + b + "\n" + string.Join("\n", pb.Diagnostics));
                    ConformanceTests.AssertParagraphs(model.Document.Paragraphs, pb.Document.Paragraphs, from + " to " + to + ":\n" + b);
                }
            }
        }

        [PandocTheory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void PandocReadsEveryFormAsWeDo(int index)
        {
            var form = Forms[index];
            var options = Options(form);
            var model = MarkdownConverter.Parse(Text, Options(MarkdownLinkForm.RootPath));
            string written = MarkdownConverter.ToMarkdown(model.Document, options);
            var actual = PandocModel.Convert(PandocRunner.ToJson(written), options);
            ConformanceTests.AssertParagraphs(model.Document.Paragraphs, actual, "Pandoc, reading form " + form + ":\n" + written);
        }
    }
}
namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    public class FixtureLineEndingTests
    {
        [Fact]
        public void AFixtureFileCheckedOutWithCrlfReadsAsTheSameCases()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fwmd-crlf-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                foreach (string path in System.IO.Directory.GetFiles(ConformanceLoader.Directory, "*.json", System.IO.SearchOption.AllDirectories))
                {
                    string lf = ConformanceLoader.ReadText(path);
                    string copy = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(path));
                    System.IO.File.WriteAllText(copy, lf.Replace("\n", "\r\n")); // what autocrlf does to the checkout
                    Assert.Equal(lf, ConformanceLoader.ReadText(copy));
                    Assert.DoesNotContain("\r", ConformanceLoader.ReadText(copy));
                }
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }

        [Fact]
        public void TheCasesThatHoldARawLineEndInAStringStillCompareWithLf()
        {
            // "an ampersand is written..." and "toggle values..." have a line end inside the JSON string; the canonical text the writer produces ends in LF.
            foreach (string name in new[] { "an ampersand is written as a reference only where it would read as one", "toggle values on off invert" })
            {
                var c = ConformanceLoader.All().Single(x => x.Name == name);
                Assert.DoesNotContain("\r", c.Canonical ?? string.Empty);
            }
        }
    }
}
namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    public class FixtureJsonTests
    {
        [Fact]
        public void EveryFixtureFileIsStrictJson()
        {
            // D6: a second implementation proves conformance by passing the same files, so they must load in any JSON parser, not only in this runner's reader (the record fixtures in records/ too).
            foreach (string path in System.IO.Directory.GetFiles(ConformanceLoader.Directory, "*.json", System.IO.SearchOption.AllDirectories))
            {
                string text = System.IO.File.ReadAllText(path);
                Json.Parse(text); // our reader rejects raw control characters in strings
#if NET10_0_OR_GREATER
                using (System.Text.Json.JsonDocument.Parse(text)) { }
#endif
            }
        }
    }
}
