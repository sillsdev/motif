using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Keeps the format documentation in docs/formats honest: every finding id the source can report has a row in the catalogue (findings.md) and every
    /// id in a table of findings.md or tables.md is in the source; every fixture marker is well-formed; and every
    /// example marked with a fixture comment is, character for character, the text of that fixture case. Both read the repository from disk, so they find the
    /// ids of every project (the LCModel adapter's too) without referencing it.</summary>
    public class FormatDocsTests
    {
        // A whole id in a string literal: "fwmd.syntax.unclosed-mark", "textediting.lcm.unstorable-property". A prefix such as "fwmd.table." ends in a dot and is
        // not an id.
        static readonly Regex IdLiteral = new Regex("\"((?:fwmd|textediting)\\.[a-z0-9.-]*[a-z0-9])\"", RegexOptions.CultureInvariant);

        // <!-- fixture: 05-links.json | case name | markdown --> before a fenced block. The field is markdown, canonical or file.
        static readonly Regex FixtureComment = new Regex(@"<!-- fixture: (?<file>[^|]+?) \| (?<name>.+?) \| (?<field>markdown|canonical|file) -->\n```[a-z]*\n(?<text>.*?)\n```",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        // <!-- fixture-excerpt: records/blocks.schema.json --> before a fenced block that is part of that fixture file, as it stands.
        static readonly Regex ExcerptComment = new Regex(@"<!-- fixture-excerpt: (?<file>[^ ]+) -->\n```[a-z]*\n(?<text>.*?)\n```",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SIL.TextEditing.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("SIL.TextEditing.sln is not above " + AppContext.BaseDirectory);
        }

        static string ReadText(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        static string FormatsDirectory => Path.Combine(RepositoryRoot(), "docs", "formats");

        // An id in the first column of a table row: | `fwmd.table.ragged` | ...
        static readonly Regex TableRowId = new Regex(@"^\| `((?:fwmd|textediting)\.[a-z0-9.-]*[a-z0-9])` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        // Any line that starts a fixture marker, well-formed or not.
        static readonly Regex AnyMarker = new Regex(@"<!--\s*fixture", RegexOptions.CultureInvariant);

        static SortedSet<string> SourceIds()
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in IdLiteral.Matches(ReadText(path))) ids.Add(m.Groups[1].Value);
            return ids;
        }

        static List<string> TableIds(string doc) => TableRowId.Matches(ReadText(Path.Combine(FormatsDirectory, doc))).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        [Fact]
        public void EveryFindingIdInTheSourceIsInTheCatalogue()
        {
            var ids = SourceIds();
            Assert.NotEmpty(ids);
            var listed = new HashSet<string>(TableIds("findings.md"), StringComparer.Ordinal);
            var missing = ids.Where(id => !listed.Contains(id)).ToList();
            Assert.True(missing.Count == 0, "docs/formats/findings.md has no table row for: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryFindingIdTheDocsListIsInTheSource()
        {
            var ids = SourceIds();
            var catalogue = new HashSet<string>(TableIds("findings.md"), StringComparer.Ordinal);
            var problems = new List<string>();
            foreach (string doc in new[] { "findings.md", "tables.md" })
            {
                var listed = TableIds(doc);
                Assert.True(listed.Count > 0, doc + " lists no finding id in a table.");
                foreach (string id in listed)
                {
                    if (!ids.Contains(id)) problems.Add(doc + ": " + id + " is not in the source");
                    if (!catalogue.Contains(id)) problems.Add(doc + ": " + id + " is not in findings.md");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void EveryFixtureMarkerIsWellFormed()
        {
            var problems = new List<string>();
            foreach (string doc in Directory.GetFiles(FormatsDirectory, "*.md"))
            {
                string text = ReadText(doc);
                int markers = AnyMarker.Matches(text).Count;
                int understood = FixtureComment.Matches(text).Count + ExcerptComment.Matches(text).Count;
                if (markers != understood)
                    problems.Add(Path.GetFileName(doc) + ": " + markers + " fixture markers, but only " + understood + " are a well-formed marker followed by a fenced block");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void EveryMarkedExampleIsTheTextOfItsFixtureCase()
        {
            int checkedExamples = 0;
            var problems = new List<string>();
            foreach (string doc in Directory.GetFiles(FormatsDirectory, "*.md"))
            {
                foreach (Match m in FixtureComment.Matches(ReadText(doc)))
                {
                    checkedExamples++;
                    string file = m.Groups["file"].Value, name = m.Groups["name"].Value, field = m.Groups["field"].Value;
                    string where = Path.GetFileName(doc) + ": " + file + " \"" + name + "\" " + field;
                    string path = Path.Combine(RepositoryRoot(), "tests", "conformance", file.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) { problems.Add(where + ": no such fixture file"); continue; }
                    var found = Json.Arr(Json.Parse(ReadText(path))).Select(Json.Obj).FirstOrDefault(c => Json.Str(c["name"]) == name);
                    if (found == null) { problems.Add(where + ": no such case"); continue; }
                    // A canonical of null means the input is canonical.
                    string? text = Json.OptStr(found, field) ?? (field == "canonical" && found.ContainsKey("canonical") ? Json.OptStr(found, "markdown") ?? Json.OptStr(found, "file") : null);
                    if (text == null) { problems.Add(where + ": the case has no " + field); continue; }
                    if (text.TrimEnd('\n') != m.Groups["text"].Value) problems.Add(where + ": the example differs from the fixture");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.True(checkedExamples > 0, "No example in docs/formats is marked with its fixture.");
        }

        [Fact]
        public void EveryMarkedExcerptIsPartOfItsFixtureFile()
        {
            var problems = new List<string>();
            foreach (string doc in Directory.GetFiles(FormatsDirectory, "*.md"))
            {
                foreach (Match m in ExcerptComment.Matches(ReadText(doc)))
                {
                    string file = m.Groups["file"].Value;
                    string path = Path.Combine(RepositoryRoot(), "tests", "conformance", file.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) problems.Add(Path.GetFileName(doc) + ": " + file + ": no such fixture file");
                    else if (ReadText(path).IndexOf(m.Groups["text"].Value, StringComparison.Ordinal) < 0) problems.Add(Path.GetFileName(doc) + ": " + file + ": the excerpt is not in the file");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
