using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;
using Xunit;
using Xunit.Abstractions;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>Opt-in: set TEXTEDITING_EXPORT_FWDATA to a .fwdata path and every lexicon entry, notebook record and list item of that project is exported and read back.
    /// The project is opened as a private temporary copy. Nothing from it is written anywhere but the temporary copy; only counts are printed.</summary>
    public class ProjectRecordExportRealDataTests
    {
        readonly ITestOutputHelper _output;
        public ProjectRecordExportRealDataTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void EveryRecordOfARealProjectExportsAndReadsBackWithOnlyKnownLosses()
        {
            string? path = Environment.GetEnvironmentVariable("TEXTEDITING_EXPORT_FWDATA");
            if (string.IsNullOrEmpty(path)) return; // not opted in
            using var fixture = new ProjectFixture(path!);
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var failures = new List<string>();
            int total = 0;
            foreach (var (kind, overlay) in new[] { ("lexicon/entry", ProjectRecordSchema.LexiconEntryOverlay), ("notebook/record", ProjectRecordSchema.NotebookRecordOverlay), ("lists/item", ProjectRecordSchema.ListItemOverlay) })
            {
                var exporter = new ProjectRecordExporter(fixture.Cache, ProjectRecordSchema.Generate(fixture.Cache, overlay));
                var exports = exporter.ExportAll(kind);
                int unexported = 0;
                foreach (var export in exports)
                {
                    total++;
                    foreach (var u in export.Unexported)
                    {
                        unexported++;
                        string reason = Category(u.Reason);
                        reasons[kind + ": " + reason] = reasons.TryGetValue(kind + ": " + reason, out int n) ? n + 1 : 1;
                    }
                    if (export.Text == null) { failures.Add(export.Guid + " not written"); continue; }
                    var read = RecordFileReader.Read(export.Text, exporter.Schema, exporter.CreateReadOptions());
                    var errors = read.Diagnostics.Where(d => d.Severity == MarkdownSeverity.Error).Select(d => d.Id).Distinct().ToList();
                    if (errors.Count > 0) failures.Add(export.Guid + " read-back errors " + string.Join(",", errors));
                    else if (!RecordDocument.AreEquivalent(export.Document, read.Document!, exporter.Schema)) failures.Add(export.Guid + " reads back unequal");
                    else
                    {
                        try { ExportOracle.Check(exporter.Cache, exporter.Result, export, read.Document!); }
                        catch (Xunit.Sdk.XunitException e) { failures.Add(export.Guid + " oracle: " + e.Message.Split('\n')[0]); }
                    }
                }
                Assert.Equal(exports.Count, exports.Select(e => e.Path).Distinct(StringComparer.Ordinal).Count());
                _output.WriteLine(kind + ": " + exports.Count + " records, " + unexported + " unexported values");
            }
            foreach (var r in reasons.OrderBy(r => r.Key, StringComparer.Ordinal)) _output.WriteLine("  " + r.Value + " x " + r.Key);
            _output.WriteLine("failures: " + failures.Count + " of " + total);
            foreach (var f in failures.Take(20)) _output.WriteLine("  " + f);
            Assert.Empty(failures);
            Assert.DoesNotContain(reasons.Keys, k => k.EndsWith("unknown", StringComparison.Ordinal));
        }


        /// <summary>Maps a reason to a name from a fixed set, so that counts never carry project text; an unrecognised reason is "unknown" and fails the test.</summary>
        static string Category(string reason)
        {
            string[,] known =
            {
                { "does not define this character style", "undefined character style" },
                { "does not define this paragraph style", "undefined paragraph style" },
                { "Direct formatting", "direct formatting not spelled" },
                { "plain text; the string's formatting", "formatting in a plain field" },
                { "could not be converted", "conversion failed" },
                { "A timestamp", "timestamp" },
                { "A general date", "general date" },
                { "Binary data", "binary" },
                { "Owned objects of this field", "owned objects outside the schema" },
                { "schema leaves this field out", "field left out by the schema" },
                { "analysis data", "analysis data" },
                { "own property is not part of the record file", "structured text's own property" },
                { "Only structured text paragraphs", "non-text paragraph" },
            };
            for (int i = 0; i < known.GetLength(0); i++)
                if (reason.Contains(known[i, 0])) return known[i, 1];
            return "unknown";
        }
    }
}
