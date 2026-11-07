using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;

// Exercises the public surface of SIL.TextEditing.Markdown under AOT, using the public API only: every block and inline form of FieldWorks Markdown with the
// default host, then the same forms and the five link forms with a host of our own that implements every optional interface. Markdig is rooted too (see
// AotCheck.csproj), so the publish analyses all of it. A failed check is a line on the console and a non-zero exit code; the publish itself is the real check,
// and this makes sure the code that publishes also runs.
internal static class MarkdownCheck
{
    static readonly List<string> Failures = new List<string>();

    static MarkdownOptions TableOptions() => new MarkdownOptions { StyleNames = Tables() };

    static MarkdownStyleNames Tables() => new MarkdownStyleNames { TableHeader = "GDR Table Header", TableRow = "GDR Table Row" };

    static void Check(bool condition, string what)
    {
        if (!condition) Failures.Add(what);
    }

    /// <summary>Runs every check; returns the failures, empty when all passed.</summary>
    public static IReadOnlyList<string> Run()
    {
        DefaultHost();
        FrontMatter();
        CustomHost();
        Paths();
        Abstract();
        return Failures;
    }

    const string Front =
        "---\nfwmd: 1\nanalysis: \"en\"\nvernacular: \"ceb\"\nwritingSystems:\n  en:\n    lang: \"en\"\n    dir: \"ltr\"\n    font: \"Charis SIL\"\n  ceb:\n    lang: \"ceb\"\n    dir: \"ltr\"\n    font: \"Charis SIL\"\n---\n\n";

    // The publish runs with invariant globalization, where string.Normalize leaves the text alone (no decomposition tables), so the check
    // below is for what the platform gives, and the code that normalises is run all the same.
    const string AllForms =
        Front
        + "# Heading one\n\n## Heading two\n\n### Heading three\n\n"
        + "Plain **bold** *italic* ***both*** [mang]{ws=ceb} [vern]{.v} [x]{custom-style=\"Emphasized Text\" fontsize=14000} [site](https://example.org) a\\\nbreak and café &amp; \\*stars\\* x^2^ H~2~O a[]{.nul}b []{.surrogate x=D800}\n\n"
        + "- bullet one\n- bullet two\n\n"
        + "1. first\n2. second\n\n"
        + "> a quote\n\n"
        + "| A | B |\n|---|---|\n| 1 | **2** |\n\n"
        + "::: {custom-style=Example fontsize=12000}\nfenced line\n\nsecond line\n:::\n\n"
        + "[]{.empty}\n\n"
        + "[]{.v .empty}\n\n"
        + "[opaque]{s6=xyz}\n";

    static void DefaultHost()
    {
        var parsed = MarkdownConverter.Parse(AllForms, TableOptions());
        Check(!parsed.HasErrors, "default host: no errors in the every-form text: " + string.Join("; ", parsed.Diagnostics));
        Check(parsed.FrontMatter != null && parsed.FrontMatter.Version == 1 && parsed.FrontMatter.Analysis == "en" && parsed.FrontMatter.WritingSystems.Count == 2, "default host: the front matter");
        var paragraphs = parsed.Document.Paragraphs;
        string[] styles = paragraphs.Select(p => p.Style).Distinct().ToArray();
        foreach (var expected in new[] { "Heading 1", "Heading 2", "Heading 3", "Normal", "Bulleted List", "Numbered List", "Block Quote", "GDR Table Header", "GDR Table Row", "Example" })
            Check(styles.Contains(expected), "default host: a paragraph of style " + expected);
        var inline = paragraphs.FirstOrDefault(p => p.Style == "Normal" && p.Runs.Count > 3);
        Check(inline != null, "default host: the inline paragraph");
        if (inline != null)
        {
            Check(inline.Runs.Any(r => r.Props.IntProps.ContainsKey(TextPropertyNumbers.Bold)), "default host: a bold run");
            Check(inline.Runs.Any(r => r.Props.IntProps.ContainsKey(TextPropertyNumbers.Italic)), "default host: an italic run");
            Check(inline.Runs.Any(r => r.Props.IntProps.ContainsKey(TextPropertyNumbers.Superscript)), "default host: superscript and subscript are read");
            Check(inline.Runs.Any(r => r.Ws == "ceb"), "default host: a span with ws");
            Check(inline.Runs.Any(r => r.ObjData != null && r.ObjData.Length > 0 && r.ObjData[0] == DefaultObjectMapper.ExternalPathNameType), "default host: an external link");
            Check(inline.Runs.Any(r => r.Text.IndexOf((char)0x2028) >= 0), "default host: a hard break");
            Check(inline.Runs.Any(r => r.Text.IndexOf('\0') >= 0) && inline.Runs.Any(r => r.Text.IndexOf('\uD800') >= 0), "default host: a NUL and a surrogate");
            Check(inline.Runs.Any(r => r.Text.Contains("caf" + "é".Normalize(System.Text.NormalizationForm.FormD))), "default host: text read as the platform normalises it");
        }
        Check(paragraphs.Any(p => p.Runs.Count == 0), "default host: an empty paragraph");
        Check(paragraphs.Any(p => p.Runs.Any(r => r.ObjData == "xyz")), "default host: generic object data");

        string written = MarkdownConverter.ToMarkdown(parsed.Document, TableOptions());
        Check(written.StartsWith("---\nfwmd: 1\n", StringComparison.Ordinal), "default host: the written text starts with the front matter");
        var again = MarkdownConverter.Parse(written, TableOptions());
        Check(!again.HasErrors, "default host: the written text reads without errors");
        Check(MarkdownConverter.AreEquivalent(parsed.Document.Paragraphs, again.Document.Paragraphs), "default host: written then read is equivalent");
        Check(MarkdownConverter.Canonicalize(written, TableOptions()) == written, "default host: the written text is canonical");
        Check(MarkdownConverter.ToMarkdown((IEnumerable<Paragraph>)paragraphs, TableOptions()) == written, "default host: ToMarkdown of the paragraphs");
        Check(MarkdownConverter.AreEquivalent(paragraphs[0], again.Document.Paragraphs[0]), "default host: a paragraph is equivalent to itself read back");
        var bodyOnly = MarkdownConverter.ToMarkdown(parsed.Document, new MarkdownOptions { WriteFrontMatter = false });
        Check(!bodyOnly.StartsWith("---", StringComparison.Ordinal), "default host: the body alone");

        // Findings of every family the parser makes without a host: syntax, table, heading, unclosed div.
        var bad = MarkdownConverter.Parse("`code` <b>x</b> [y]{wss=1} [z]{bold=on bold=off} [w]{.vern}\n\n#### deep\n\n| a | b |\n|:---|---|\n| 1 |\n\n::: {custom-style=x}\nunclosed");
        foreach (var id in new[] { MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownDiagnosticIds.UnknownAttribute, MarkdownDiagnosticIds.DuplicateAttribute, MarkdownDiagnosticIds.HeadingTooDeep,
                     MarkdownDiagnosticIds.TableAlignment, MarkdownDiagnosticIds.TableRagged, MarkdownDiagnosticIds.UnclosedMark })
            Check(bad.Diagnostics.Any(d => d.Id == id), "default host: finding " + id);
        Check(bad.HasErrors && bad.Diagnostics.All(d => d.Line > 0 && d.Column > 0), "default host: findings have a place");
        var made = bad.CreateDiagnostic("host.own", MarkdownSeverity.Warning, "mine", 0, 4, "x", new[] { "a" }, new[] { "b" });
        Check(made.Observed == "`cod" && made.Line == 1, "default host: a finding of the host's own");

        // A text beyond Markdig's depth limit is a finding and plain text, not an exception.
        var deep = MarkdownConverter.Parse(new string('*', 5000) + "x");
        Check(deep.Document.Paragraphs.Count > 0, "default host: deeply nested text is kept");

        // Other style names.
        var names = new MarkdownStyleNames { Normal = "Body", Heading1 = "H1", BulletedList = "Bullets", TableRow = "Row", TableHeader = "Head", Hyperlink = "Link" };
        var renamed = MarkdownConverter.Parse("# a\n\n- b\n\nc [d](https://e.org)", new MarkdownOptions { StyleNames = names });
        Check(renamed.Document.Paragraphs.Select(p => p.Style).SequenceEqual(new[] { "H1", "Bullets", "Body" }), "default host: renamed styles");
    }

    static void FrontMatter()
    {
        var newer = MarkdownConverter.Parse("---\nfwmd: 2\n---\n\nx\n");
        Check(newer.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.VersionUnsupported) && newer.Document.Paragraphs.Count == 0, "front matter: a newer version is refused");
        var defaults = MarkdownConverter.Parse("---\nfwmd: 1\nanalysis: \"ceb\"\nvernacular: \"en\"\n---\n\nx [y]{.v}\n");
        Check(defaults.Document.Paragraphs[0].Runs.Select(r => r.Ws).SequenceEqual(new[] { "ceb", "en" }), "front matter: its defaults name unmarked text and .v");
        var info = WritingSystemInfo.Default("ar-Arab");
        Check(info.Direction == "rtl" && info.Language == "ar-Arab" && info.Font.Length > 0, "front matter: a default description of a writing system");
        var host = new CheckHost();
        var options = new MarkdownOptions { WritingSystems = host };
        var document = new Document();
        var paragraph = new Paragraph { Style = "Normal" };
        paragraph.Runs.Add(new Run { Text = "mmm", Ws = "ar" });
        paragraph.DefaultWs = "ar";
        document.Paragraphs.Add(paragraph);
        string text = MarkdownConverter.ToMarkdown(document, options);
        Check(text.Contains("  ar:\n    lang: \"ar\"\n    dir: \"rtl\"\n    font: \"Scheherazade New\"\n"), "front matter: the host describes the writing systems the text uses");
    }

    static void CustomHost()
    {
        var host = new CheckHost();
        var options = new MarkdownOptions { WritingSystems = host, Styles = host, Objects = host, Paths = host, StyleNames = Tables(), WriteFrontMatter = false };
        string md = "Know [palit](/lexicon/palit(2)){.name guid=" + CheckHost.Entry + "} and [a rule](</grammar/phonological-rules/Nasal assimilation>){guid=" + CheckHost.Rule + "} and [by path](/lexicon/palit(2)) and ![a picture](/files/pictures/hut.jpg){guid=" + CheckHost.Picture + "}\n\n"
            + "::: {custom-style=Example}\n[mang]{ws=ceb} [old]{ws=old}\n:::\n";
        var parsed = MarkdownConverter.Parse(md, options);
        Check(parsed.Diagnostics.All(d => d.Severity != MarkdownSeverity.Error), "custom host: no errors: " + string.Join("; ", parsed.Diagnostics));
        Check(parsed.Objects.Count == 4, "custom host: four objects, got " + parsed.Objects.Count);
        Check(parsed.Objects.All(o => o.Source != null && o.ObjData != null), "custom host: each object has a place and data");
        Check(parsed.Objects.Any(o => o.Kind == MarkdownObjectKind.InternalLink && o.FollowsName), "custom host: a link that follows the name");
        Check(parsed.Document.Paragraphs[0].Runs.Any(r => r.ObjData == CheckHost.PictureData), "custom host: the picture's data");
        Check(parsed.Diagnostics.Any(d => d.Id == "host.ws.legacy" && d.Severity == MarkdownSeverity.Warning), "custom host: the host's warning for a tag");

        foreach (var form in new[] { MarkdownLinkForm.RootPath, MarkdownLinkForm.FileRelative, MarkdownLinkForm.SilfwUrl, MarkdownLinkForm.MotifUrl, MarkdownLinkForm.ObjectAlias })
        {
            var formOptions = new MarkdownOptions { WritingSystems = host, Styles = host, Objects = host, Paths = host, StyleNames = Tables(), WriteFrontMatter = false, LinkForm = form, LinkBase = "/lists/GDR/GDR-47.fw.md", ProjectName = "Sena3" };
            var written = MarkdownConverter.ToMarkdown(parsed.Document, formOptions);
            var back = MarkdownConverter.Parse(written, formOptions);
            Check(!back.HasErrors, "custom host: " + form + " reads without errors: " + string.Join("; ", back.Diagnostics));
            Check(MarkdownConverter.AreEquivalent(parsed.Document.Paragraphs, back.Document.Paragraphs), "custom host: " + form + " written then read is equivalent");
        }

        // The host's problems: unknown writing system and style, an ambiguous and a missing link, a deleted target, a stale path, a missing picture, a mismatch.
        var bad = MarkdownConverter.Parse("[x]{ws=zzz} [a](/lexicon/palt) [b](/lexicon/twin) [c](/lexicon/gone){.broken guid=" + CheckHost.Gone + "} [d](/lexicon/other){guid=" + CheckHost.Entry + "} ![p](/files/x.jpg){guid=" + Guid.Empty + "} [e](/objects/" + CheckHost.Entry + "){guid=" + CheckHost.Rule + "}\n\n::: {custom-style=Exmple}\n[y]{custom-style=Strng}\n:::", options);
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.WsUnknown), "custom host: unknown writing system");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkNotFound), "custom host: link not found");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkAmbiguous && d.Choices.Count == 2), "custom host: ambiguous link with choices");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkBroken && d.Severity == MarkdownSeverity.Warning), "custom host: a deleted target is a warning");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkStalePath), "custom host: a stale path");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkGuidMismatch), "custom host: a GUID mismatch");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.PictureUnknown), "custom host: unknown picture");
        Check(bad.Diagnostics.Count(d => d.Id == MarkdownDiagnosticIds.StyleUnknown) == 2, "custom host: unknown styles");
        var rebind = new MarkdownOptions { WritingSystems = host, Objects = host, Paths = host, UnknownGuids = UnknownGuidPolicy.RebindByPath };
        var rebound = MarkdownConverter.Parse("[x](/lexicon/palit(2)){guid=" + Guid.Empty + "}", rebind);
        Check(rebound.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkRebound), "custom host: a GUID rebound by its path");
        var relative = MarkdownConverter.Parse("[x](../../lexicon/palit(2).fw.md)", options);
        Check(relative.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkRelativeWithoutBase), "custom host: a relative link without a base");

        // A host with only the required interfaces.
        var plain = new MarkdownOptions { WritingSystems = new PlainSystems(), Styles = new PlainStyles(), Objects = new PlainObjects() };
        var plainResult = MarkdownConverter.Parse("[x]{ws=zzz}\n\n::: {custom-style=nope}\n[a](https://e.org) [b](/objects/" + CheckHost.Entry + ")\n:::", plain);
        Check(plainResult.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.WsUnknown), "plain host: unknown writing system");
        Check(plainResult.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.StyleUnknown), "plain host: unknown style");
        Check(plainResult.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkNotFound), "plain host: a link it cannot make");
        Check(new MarkdownProblem("m").Severity == MarkdownSeverity.Error && new MarkdownSourceRange(0, 1, 1, 1, 1, 2).End == 1, "the small public types");
    }

    static void Paths()
    {
        var path = ProjectPath.Parse("/lexicon/palit(2)/senses/1#definition.en");
        Check(path != null && path.Segments.Count == 4 && path.Segments[1].Homograph == 2 && path.Field == "definition" && path.FieldWritingSystem == "en", "paths: parsed with a homograph and a field");
        Check(path != null && path.ToString() == "/lexicon/palit(2)/senses/1#definition.en", "paths: written as read");
        Check(new ProjectPath("lexicon", "AC/DC").ToString() == "/lexicon/AC%2FDC", "paths: a slash is encoded");
        // Under invariant globalization (as in this publish) there are no decomposition tables, so canonical equivalence is only what the platform gives.
        bool normalises = "é".Normalize(System.Text.NormalizationForm.FormD).Length == 2;
        Check(!normalises || ProjectPath.Parse("/lexicon/café")!.Equals(ProjectPath.Parse("/lexicon/café")), "paths: equal under canonical equivalence");
        Check(ProjectPath.Parse("/lexicon/café")!.Equals(ProjectPath.Parse("/lexicon/café")), "paths: equal to itself");
        var guid = new Guid("7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d");
        string url = SilfwUrl.Build(guid, "lexiconEdit");
        Check(SilfwUrl.TryParse(url, out var parts) && parts.Guid == guid && parts.Tool == "lexiconEdit" && parts.Database == SilfwUrl.CurrentProject, "paths: a silfw URL is built and read");
        Check(ProjectPath.ForObject(guid).ObjectGuid == guid, "paths: the GUID alias");
    }

    static void Abstract()
    {
        var result = MarkdownConverter.ParseInline("An **abstract** with [mang]{ws=ceb}\\\nand a break\n\n# not allowed");
        Check(result.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.FieldBlockSyntax) && result.HasErrors, "abstract: block syntax is a finding");
        Check(result.Runs.Any(r => r.Ws == "ceb"), "abstract: a span");
        string text = MarkdownConverter.ToInlineMarkdown(result.Runs);
        Check(text.Contains("**abstract**"), "abstract: written back inline");
        Check(result.Source.Length > 0 && result.CreateDiagnostic("x.y", MarkdownSeverity.Suggestion, "m", 0, 2).Observed == "An", "abstract: a finding of the host's own");
    }

    sealed class CheckHost : IWritingSystemMapper, IKnownWritingSystems, IWritingSystemChecker, IWritingSystemDetails, IStyleCatalog, IKnownStyles, IStyleChecker, IObjectMapper, IObjectResolver, IProjectPathResolver
    {
        public static readonly Guid Entry = new Guid("11111111-2222-3333-4444-555555555555");
        public static readonly Guid Rule = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        public static readonly Guid Picture = new Guid("99999999-8888-7777-6666-555555555555");
        public static readonly Guid Gone = new Guid("deadbeef-0000-4000-8000-000000000005");
        public static readonly string PictureData = "\u0008" + Picture.ToString("N");

        static readonly string[] Tags = { "en", "ceb", "ar", "old" };
        static readonly string[] ParagraphStyles = { "Normal", "Example" };
        static readonly string[] CharacterStyles = { "Emphasized Text", "Strong", "Hyperlink" };

        public string DefaultAnalysis => "en";
        public string DefaultVernacular => "ceb";
        public bool IsKnown(string tag) => Array.IndexOf(Tags, tag) >= 0;
        public IReadOnlyList<string> KnownTags => Tags;
        public WritingSystemInfo? DescribeWritingSystem(string id) => id == "ar" ? new WritingSystemInfo("ar", "rtl", "Scheherazade New") : null;

        public MarkdownProblem? CheckWritingSystem(string tag)
        {
            if (tag == "old") return new MarkdownProblem("host.ws.legacy", MarkdownSeverity.Warning, "'old' is a legacy writing system.");
            return IsKnown(tag) ? null : new MarkdownProblem("'" + tag + "' is not a writing system of this project.");
        }

        public bool IsParagraphStyle(string name) => Array.IndexOf(ParagraphStyles, name) >= 0;
        public bool IsCharacterStyle(string name) => Array.IndexOf(CharacterStyles, name) >= 0;
        public IReadOnlyList<string> ParagraphStyleNames => ParagraphStyles;
        public IReadOnlyList<string> CharacterStyleNames => CharacterStyles;

        public MarkdownProblem? CheckStyle(string name, bool paragraph)
            => (paragraph ? IsParagraphStyle(name) : IsCharacterStyle(name)) ? null : new MarkdownProblem("'" + name + "' is not a style of this project.");

        // The project: three objects with paths, one deleted, a picture, and two entries that share a name.
        public Guid? Resolve(ProjectPath path, out IReadOnlyList<Guid> candidates)
        {
            candidates = Array.Empty<Guid>();
            string text = new ProjectPath(path.Segments).ToString();
            if (text == "/lexicon/palit(2)") return Entry;
            if (text == "/grammar/phonological-rules/Nasal assimilation") return Rule;
            if (text == "/lexicon/twin") { candidates = new[] { Guid.NewGuid(), Guid.NewGuid() }; return null; }
            return null;
        }

        public ProjectPath? PathOf(Guid guid)
            => guid == Entry ? ProjectPath.Parse("/lexicon/palit(2)") : guid == Rule ? ProjectPath.Parse("/grammar/phonological-rules/Nasal assimilation") : null;

        public string? ToolOf(Guid guid) => guid == Entry ? "lexiconEdit" : guid == Rule ? "PhonologicalRuleEdit" : null;

        public MarkdownObject? Describe(string objData)
        {
            if (objData == PictureData) return new MarkdownObject { Kind = MarkdownObjectKind.Picture, Guid = Picture, Caption = "a picture", Path = "/files/pictures/hut.jpg" };
            var described = DefaultObjectMapper.Instance.Describe(objData);
            if (described != null && described.Guid == Entry) described.Name = "palit";
            return described;
        }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            bool ok = TryResolve(target, out objData, out var p);
            problem = p?.Message;
            return ok;
        }

        public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
        {
            problem = null;
            objData = string.Empty;
            if (target.Kind == MarkdownObjectKind.Picture)
            {
                if (target.Guid != Picture) { problem = new MarkdownProblem("no such picture"); return false; }
                objData = PictureData;
                return true;
            }
            return DefaultObjectMapper.Instance.TryCreateObjData(target, out objData, out string? reason) ? true : Fail(reason, out problem);
        }

        static bool Fail(string? reason, out MarkdownProblem? problem)
        {
            problem = new MarkdownProblem(reason ?? "cannot");
            return false;
        }
    }

    sealed class PlainSystems : IWritingSystemMapper
    {
        public string DefaultAnalysis => "en";
        public string DefaultVernacular => "ceb";
        public bool IsKnown(string tag) => tag == "en" || tag == "ceb";
    }

    sealed class PlainStyles : IStyleCatalog
    {
        public bool IsParagraphStyle(string name) => name == "Normal";
        public bool IsCharacterStyle(string name) => name == "Strong";
    }

    sealed class PlainObjects : IObjectMapper
    {
        public MarkdownObject? Describe(string objData) => null;

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            objData = string.Empty;
            problem = "nothing resolves";
            return false;
        }
    }
}
