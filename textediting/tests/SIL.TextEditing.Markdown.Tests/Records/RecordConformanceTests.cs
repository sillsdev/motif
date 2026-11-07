using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>Runs the record-file fixtures in tests/conformance/records (README.md there, "Record files"): text in, expected document and findings out; the
    /// canonical text written back from what was read; a write case's document to its canonical text.</summary>
    public class RecordConformanceTests
    {
        public static IEnumerable<object[]> Cases() => RecordFixtures.All().Select(c => new object[] { c.Id });

        [Theory]
        [MemberData(nameof(Cases))]
        public async System.Threading.Tasks.Task RecordFixtureCase(string id)
        {
            var c = RecordFixtures.Find(id);
            try
            {
                // A case that does not finish fails instead of hanging the run: the reader must end on any text (review of PR 147, P1-1). This is a limit,
                // not a measurement; a case takes milliseconds.
                var run = System.Threading.Tasks.Task.Run(() => { if (c.IsRead) RunRead(c); else RunWrite(c); });
                var first = await System.Threading.Tasks.Task.WhenAny(run, System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(2)));
                if (first != run) Assert.Fail("The case did not finish.");
                await run;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // xunit shortens a long case id in the test name, so the message starts with the whole id.
                throw new Xunit.Sdk.XunitException("CASE " + c.Id + "\n" + e.Message + "\n" + e.StackTrace);
            }
        }

        [Fact]
        public void EveryFindingIdHasAPositiveAndANegativeCase()
        {
            var all = RecordFixtures.All();
            var missing = new List<string>();
            foreach (string id in RecordDiagnosticIds.All)
            {
                bool positive = all.Any(c => c.IsRead && c.Findings.Any(f => f.Id == id));
                bool negative = all.Any(c => c.IsRead && !c.Findings.Any(f => f.Id == id) && NegativeIds(c.Name).Contains(id));
                if (!positive || !negative) missing.Add(id + (positive ? string.Empty : " (no positive case)") + (negative ? string.Empty : " (no negative case, named \"ok: " + id + ": ...\")"));
            }
            Assert.True(missing.Count == 0, string.Join("\n", missing));
        }

        /// <summary>The ids a negative case names: <c>"ok: fwmd.yaml.anchor fwmd.yaml.alias: quoted &amp; and * are text"</c> names two.</summary>
        static HashSet<string> NegativeIds(string name)
        {
            if (!name.StartsWith("ok: ", StringComparison.Ordinal)) return new HashSet<string>();
            string rest = name.Substring(4);
            int colon = rest.IndexOf(": ", StringComparison.Ordinal);
            if (colon >= 0) rest = rest.Substring(0, colon);
            return new HashSet<string>(rest.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }

        [Fact]
        public void EveryCanonicalCaseIsReadByPandocOrSaysWhyNot()
        {
            var unexplained = RecordFixtures.All().Where(c => c.CanonicalText != null && !c.Pandoc && c.PandocReason == null).Select(c => c.Id).ToList();
            Assert.True(unexplained.Count == 0, string.Join("\n", unexplained));
        }

        static void RunRead(RecordCase c)
        {
            var result = RecordFileReader.Read(c.Text!, c.Schema, RecordFixtures.Options(c));
            AssertFindings(c, result.Diagnostics);
            if (c.Document != null)
            {
                Assert.NotNull(result.Document);
                var expected = RecordFixtures.BuildDocument(c.Document, c.Schema);
                AssertDocuments(expected, result.Document!, c.Schema, "Read");
            }
            foreach (var kv in c.Paragraphs)
            {
                var value = result.Document!.Find(kv.Key) as RecordScalar;
                Assert.True(value?.Paragraphs != null, "No rich value at " + kv.Key);
                ConformanceTests.AssertParagraphs(kv.Value, value!.Paragraphs!, "The rich value at " + kv.Key);
            }
            foreach (var kv in c.Targets)
            {
                var value = result.Document!.Find(kv.Key) as RecordScalar;
                Assert.True(value != null, "No reference at " + kv.Key);
                Assert.Equal(kv.Value, value!.Target?.ToString("D"));
            }
            if (c.CanonicalSpecified)
            {
                Assert.False(result.HasErrors, "A case with a canonical form has no errors.");
                string written = RecordFileWriter.Write(result.Document!, c.Schema);
                Assert.Equal(c.CanonicalText, written);
                var again = RecordFileReader.Read(written, c.Schema, RecordFixtures.Options(c));
                Assert.True(again.Diagnostics.Count == 0, "The canonical text has findings:\n" + string.Join("\n", again.Diagnostics));
                AssertDocuments(result.Document!, again.Document!, c.Schema, "Reading the canonical text");
                Assert.Equal(written, RecordFileWriter.Write(again.Document!, c.Schema));
            }
        }

        static void RunWrite(RecordCase c)
        {
            Assert.True(c.Document != null && c.CanonicalSpecified && c.Canonical != null, "A write case has a document and a canonical string.");
            var document = RecordFixtures.BuildDocument(c.Document!, c.Schema);
            string written = RecordFileWriter.Write(document, c.Schema);
            Assert.Equal(c.Canonical, written);
            var back = RecordFileReader.Read(written, c.Schema, RecordFixtures.Options(c));
            Assert.True(back.Diagnostics.Count == 0, "The canonical text has findings:\n" + string.Join("\n", back.Diagnostics));
            AssertDocuments(document, back.Document!, c.Schema, "Reading the canonical text");
        }

        internal static void AssertDocuments(RecordDocument expected, RecordDocument actual, RecordSchema schema, string what)
        {
            if (RecordDocument.AreEquivalent(expected, actual)) return;
            Assert.Fail(what + ": the documents differ.\nExpected:\n" + RecordDump.Of(expected) + "\nActual:\n" + RecordDump.Of(actual));
        }

        static void AssertFindings(RecordCase c, IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            var actual = diagnostics.Select(d => new ExpectedFinding
            {
                Id = d.Id,
                Severity = d.Severity == MarkdownSeverity.Error ? "error" : d.Severity == MarkdownSeverity.Warning ? "warning" : "suggestion",
                Line = d.Line, Column = d.Column, EndLine = d.EndLine, EndColumn = d.EndColumn,
            }).ToList();
            // A syntax error's place is the YAML parser's own, which another implementation need not share: only its id and line are compared.
            string Compared(ExpectedFinding f) => f.Id == RecordDiagnosticIds.YamlSyntax ? f.Id + " " + f.Severity + " line " + f.Line : f.ToString();
            var expectedText = c.Findings.Select(Compared).ToList();
            var actualText = actual.Select(Compared).ToList();
            Assert.True(expectedText.SequenceEqual(actualText), "Findings differ.\nExpected:\n" + string.Join("\n", expectedText) + "\nActual:\n" + string.Join("\n", actualText)
                + "\nAs fixture JSON:\n" + FixtureJson(diagnostics) + "\nMessages:\n" + string.Join("\n", diagnostics.Select(d => d.Id + " [" + d.Severity + "] " + d.Message)));
            for (int i = 0; i < c.Findings.Count; i++)
            {
                var e = c.Findings[i];
                var d = diagnostics[i];
                if (e.HasFix) Assert.True(e.Fix == d.Replacement, "Finding " + (i + 1) + " (" + e.Id + "): fix " + Show(e.Fix) + " expected, " + Show(d.Replacement) + " given.");
                if (e.Choices != null) Assert.Equal(e.Choices, d.Choices.ToList());
                if (e.Nearest != null) Assert.Equal(e.Nearest, d.Nearest.ToList());
            }
        }

        static string Show(string? s) => s == null ? "null" : "\"" + s.Replace("\n", "\\n") + "\"";

        /// <summary>The findings as fixture JSON, so a new case's expectation is written from what the reader says after it has been checked by eye.</summary>
        static string FixtureJson(IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            var sb = new StringBuilder();
            foreach (var d in diagnostics)
            {
                sb.Append("{ \"id\": \"").Append(d.Id).Append('"');
                if (d.Severity != MarkdownSeverity.Error) sb.Append(", \"severity\": \"").Append(d.Severity == MarkdownSeverity.Warning ? "warning" : "suggestion").Append('"');
                sb.Append(", \"line\": ").Append(d.Line).Append(", \"column\": ").Append(d.Column).Append(", \"endLine\": ").Append(d.EndLine).Append(", \"endColumn\": ").Append(d.EndColumn);
                sb.Append(", \"fix\": ").Append(d.Replacement == null ? "null" : JsonString(d.Replacement));
                if (d.Choices.Count > 0) sb.Append(", \"choices\": [").Append(string.Join(", ", d.Choices.Select(JsonString))).Append(']');
                if (d.Nearest.Count > 0) sb.Append(", \"nearest\": [").Append(string.Join(", ", d.Nearest.Select(JsonString))).Append(']');
                sb.Append(" },\n");
            }
            return sb.ToString();
        }

        internal static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                if (ch == '"') sb.Append("\\\"");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch < ' ' || ch > '~') sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>A record document as indented text, for failure messages.</summary>
    internal static class RecordDump
    {
        public static string Of(RecordDocument d)
        {
            var sb = new StringBuilder();
            sb.Append("record=").Append(d.Kind).Append(" root=").Append(d.Root).Append(" base=").Append(d.Base).Append(" analysis=").Append(d.Analysis).Append(" vernacular=").Append(d.Vernacular).Append('\n');
            foreach (var w in d.WritingSystems) sb.Append("ws ").Append(w.Key).Append(' ').Append(w.Value.Language).Append(' ').Append(w.Value.Direction).Append(' ').Append(w.Value.Font).Append('\n');
            Value(sb, d.Data, 0);
            return sb.ToString();
        }

        static void Value(StringBuilder sb, RecordValue v, int indent)
        {
            switch (v)
            {
                case RecordScalar s: sb.Append(RecordConformanceTests.JsonString(s.Text)).Append('\n'); break;
                case RecordAlternatives a:
                    sb.Append('\n');
                    foreach (var i in a.Items) { sb.Append(' ', indent + 2).Append(i.Key).Append(": "); Value(sb, i.Value, indent + 2); }
                    break;
                case RecordList l:
                    sb.Append('\n');
                    foreach (var i in l.Items) { sb.Append(' ', indent + 2).Append("- "); Value(sb, i, indent + 4); }
                    break;
                case RecordObject o:
                    sb.Append(o.Guid == null ? "{" : "{guid " + o.Guid).Append("}\n");
                    foreach (var f in o.Fields) { sb.Append(' ', indent + 2).Append(f.Key).Append(": "); Value(sb, f.Value, indent + 2); }
                    break;
            }
        }
    }
}
