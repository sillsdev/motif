using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>One expected finding of a fixture: <c>id</c>, <c>severity</c> and the range, and optionally the fix, choices and nearest names.</summary>
    internal sealed class ExpectedFinding
    {
        public string Id = string.Empty, Severity = "error";
        public int Line, Column, EndLine, EndColumn;
        public bool HasFix;
        public string? Fix;
        public List<string>? Choices, Nearest;

        public override string ToString() => Id + " " + Severity + " " + Line + ":" + Column + "-" + EndLine + ":" + EndColumn;
    }

    /// <summary>One case of a conformance fixture file (tests/conformance/README.md).</summary>
    internal sealed class ConformanceCase
    {
        public string File = string.Empty, Name = string.Empty;
        public string Id => File + "::" + Name;
        public string? Markdown;
        public List<Paragraph>? Paragraphs;
        public List<ExpectedFinding> Findings = new List<ExpectedFinding>();
        public bool CanonicalSpecified;
        public string? Canonical;
        public bool ExplicitFrontMatter;
        /// <summary>"inline" for the multi-string field grammar (<c>ParseInline</c>, <c>ToInlineMarkdown</c>): one paragraph of inline text, no front matter, no blocks.</summary>
        public bool Inline;
        public bool Pandoc = true;
        public string? PandocReason;
        public Dictionary<string, object?>? Project;
        public Dictionary<string, object?>? Options;

        public bool IsRead => Markdown != null;
        public bool HasErrors => Findings.Any(f => f.Severity == "error");

        /// <summary>The front matter the runner puts before the Markdown of a case that does not say <c>"frontMatter": "explicit"</c>.</summary>
        public const string StandardFrontMatter =
            "---\nfwmd: 1\nanalysis: \"en\"\nvernacular: \"ceb\"\nwritingSystems:\n  en:\n    lang: \"en\"\n    dir: \"ltr\"\n    font: \"Charis SIL\"\n  ceb:\n    lang: \"ceb\"\n    dir: \"ltr\"\n    font: \"Charis SIL\"\n---\n\n";

        public static readonly int StandardFrontMatterLines = StandardFrontMatter.Count(c => c == '\n');

        public string FullMarkdown(string body) => ExplicitFrontMatter ? body : StandardFrontMatter + body;
    }

    internal static class ConformanceLoader
    {
        static List<ConformanceCase>? _all;

        /// <summary>A fixture file's text with LF line ends, whatever the checkout did to it: the canonical Markdown the fixtures hold uses LF (spec 6.1), and a raw line end inside a JSON string is part of that text.</summary>
        /// <summary>A fixture file's text with LF line ends, whatever the checkout did to it: the canonical Markdown the fixtures hold uses LF (spec 6.1), and a raw line end
        /// inside a JSON string is part of that text.</summary>
        public static string ReadText(string path) => File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

        public static string Directory =>Path.Combine(AppContext.BaseDirectory, "conformance");

        public static IReadOnlyList<ConformanceCase> All()
        {
            if (_all != null) return _all;
            var cases = new List<ConformanceCase>();
            foreach (string path in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Path.GetFileName(path) == "projects.json") continue;
                var list = Json.Arr(Json.Parse(ReadText(path)));
                foreach (var item in list) cases.Add(Load(Path.GetFileName(path), Json.Obj(item)));
            }
            var duplicate = cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidOperationException("Two fixture cases are called " + duplicate.Key);
            return _all = cases;
        }

        public static ConformanceCase Find(string id) => All().First(c => c.Id == id);

        static bool StartsWithFrontMatter(string? text)
            => text != null && (text.StartsWith("---\n", StringComparison.Ordinal) || text.StartsWith(((char)0xFEFF) + "---\n", StringComparison.Ordinal));


        static ConformanceCase Load(string file, Dictionary<string, object?> o)
        {
            var c = new ConformanceCase { File = file, Name = Json.Str(o["name"]) };
            c.Markdown = Json.OptStr(o, "markdown");
            c.ExplicitFrontMatter = Json.OptStr(o, "frontMatter") == "explicit";
            c.Inline = Json.OptStr(o, "mode") == "inline";
            if (c.Inline) c.ExplicitFrontMatter = true;
            if (o.TryGetValue("paragraphs", out var ps) && ps != null) c.Paragraphs = Json.Arr(ps).Select(p => Paragraph(Json.Obj(p))).ToList();
            if (o.TryGetValue("findings", out var fs) && fs != null)
            {
                foreach (var f in Json.Arr(fs).Select(Json.Obj))
                {
                    var finding = new ExpectedFinding
                    {
                        Id = Json.Str(f["id"]),
                        Severity = Json.OptStr(f, "severity") ?? "error",
                        Line = Json.OptInt(f, "line", 1),
                        Column = Json.OptInt(f, "column", 1),
                        EndLine = Json.OptInt(f, "endLine", Json.OptInt(f, "line", 1)),
                        EndColumn = Json.OptInt(f, "endColumn", 1),
                        HasFix = f.ContainsKey("fix"),
                        Fix = Json.OptStr(f, "fix"),
                    };
                    if (f.TryGetValue("choices", out var ch) && ch != null) finding.Choices = Json.Arr(ch).Select(Json.Str).ToList();
                    if (f.TryGetValue("nearest", out var ne) && ne != null) finding.Nearest = Json.Arr(ne).Select(Json.Str).ToList();
                    c.Findings.Add(finding);
                }
            }
            if (o.ContainsKey("canonical")) { c.CanonicalSpecified = true; c.Canonical = Json.OptStr(o, "canonical"); }
            if (o.TryGetValue("pandoc", out var pd))
            {
                if (pd is bool b) c.Pandoc = b;
                else if (pd is string reason) { c.Pandoc = false; c.PandocReason = reason; }
            }
            if (o.TryGetValue("project", out var pr) && pr != null) c.Project = pr is string name ? Json.Obj(Projects()[name]) : Json.Obj(pr);
            if (o.TryGetValue("options", out var op) && op != null) c.Options = Json.Obj(op);
            // A case written the way spec 6.3 shows it, with its front matter inside the text, is a whole file: the runner adds nothing.
            if (StartsWithFrontMatter(c.Markdown) || (c.Markdown == null && StartsWithFrontMatter(c.Canonical))) c.ExplicitFrontMatter = true;
            return c;
        }

        static Dictionary<string, object?>? _projects;

        /// <summary>The named projects of <c>projects.json</c>, which a case names in its <c>project</c> field instead of repeating.</summary>
        static Dictionary<string, object?> Projects()
            => _projects ?? (_projects = Json.Obj(Json.Parse(ReadText(Path.Combine(Directory, "projects.json")))));

        /// <summary>A run's object data: spelled out (<c>objData</c>), or as <c>link</c> (a project link: the <c>silfw://</c> URL FieldWorks stores, built from the GUID,
        /// tool and optional tag, or given whole as <c>url</c>), or as <c>picture</c> (the GUID of a picture the project holds).</summary>
        static string? ObjData(Dictionary<string, object?> run)
        {
            if (run.TryGetValue("link", out var l) && l != null)
            {
                var link = Json.Obj(l);
                string url = Json.OptStr(link, "url") ?? SilfwUrl.Build(Guid.Parse(Json.Str(link["guid"])), Json.OptStr(link, "tool"), Json.OptStr(link, "tag"), Json.OptStr(link, "database"));
                return DefaultObjectMapper.ExternalPathNameType + url;
            }
            if (run.TryGetValue("picture", out var p) && p != null) return FixtureHost.PictureData(Guid.Parse(Json.Str(p)));
            return Json.OptStr(run, "objData");
        }

        // ---------------------------------------------------------------- the model

        internal static Paragraph Paragraph(Dictionary<string, object?> o)
        {
            string style = Json.OptStr(o, "style") ?? "Normal";
            var paragraph = new Paragraph { Style = style };
            if (o.TryGetValue("props", out var props) && props != null) Props(Json.Obj(props), paragraph.Props);
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            paragraph.Rtl = paragraph.Props.TryGetInt(128, out int rtl) && rtl != 0;
            if (o.TryGetValue("runs", out var runs) && runs != null)
                foreach (var r in Json.Arr(runs).Select(Json.Obj)) paragraph.Runs.Add(Run(r));
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : Json.OptStr(o, "typingWs") ?? "en";
            return paragraph;
        }

        static Run Run(Dictionary<string, object?> o)
        {
            var run = new Run { Text = Json.Str(o["text"]), Ws = Json.Str(o["ws"]), ObjData = ObjData(o) };
            if (o.TryGetValue("props", out var props) && props != null) Props(Json.Obj(props), run.Props);
            return run;
        }

        sealed class Collector : IAttrIssues
        {
            public readonly List<string> Problems = new List<string>();
            public void Report(string id, MarkdownSeverity severity, string message, AttrItem item, string? replacement = null) { if (severity == MarkdownSeverity.Error) Problems.Add(id + ": " + message); }
        }

        /// <summary>The properties a fixture spells with their attribute keys (<c>bold</c>, <c>fontsize</c>, <c>p50</c>, <c>s6x</c>) as a property bag.</summary>
        static void Props(Dictionary<string, object?> spelled, PropertyBag bag)
        {
            var items = spelled.Select(kv => new AttrItem(kv.Key, Json.Str(kv.Value), false, 0, 0)).ToList();
            var collector = new Collector();
            PropertyKeys.Apply(items, bag, collector, item => collector.Problems.Add("unknown key " + item.Key));
            if (collector.Problems.Count > 0) throw new InvalidOperationException("A fixture's properties are not valid: " + string.Join("; ", collector.Problems));
        }
    }
}
