using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using Xunit;
using Xunit.Abstractions;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>Opt-in: set TEXTEDITING_EXPORT_FWDATA to a .fwdata path. Every lexicon entry, notebook record and list item of that project is exported, planned unedited and counted:
    /// a no-op file must plan no change and no refusal. The project is opened as a private temporary copy; only counts are printed, never project content.</summary>
    public class ProjectRecordImportRealDataTests
    {
        readonly ITestOutputHelper _output;
        public ProjectRecordImportRealDataTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void ANoOpFileForEveryRecordOfARealProjectPlansNoChangeAndNoRefusal()
        {
            string? path = Environment.GetEnvironmentVariable("TEXTEDITING_EXPORT_FWDATA");
            if (string.IsNullOrEmpty(path)) { _output.WriteLine("not opted in: TEXTEDITING_EXPORT_FWDATA is not set"); return; }
            using var fixture = new ProjectFixture(path!);
            var failures = new List<string>();
            var refusalKinds = new Dictionary<RecordRefusalKind, int>();
            int total = 0, changes = 0, refusals = 0, stale = 0;
            foreach (var (kind, overlay) in new[] { ("lexicon/entry", ProjectRecordSchema.LexiconEntryOverlay), ("notebook/record", ProjectRecordSchema.NotebookRecordOverlay), ("lists/item", ProjectRecordSchema.ListItemOverlay) })
            {
                var exporter = new ProjectRecordExporter(fixture.Cache, ProjectRecordSchema.Generate(fixture.Cache, overlay));
                var importer = new ProjectRecordImporter(exporter);
                int count = 0;
                foreach (var export in exporter.ExportAll(kind))
                {
                    count++;
                    total++;
                    var plan = importer.Plan(export.Text!);
                    changes += plan.ChangeSet.Changes.Count;
                    refusals += plan.ChangeSet.Refusals.Count;
                    if (plan.BaseVerdict != ProjectRecordBaseVerdict.Fresh) stale++;
                    foreach (var r in plan.ChangeSet.Refusals) refusalKinds[r.Kind] = refusalKinds.TryGetValue(r.Kind, out int n) ? n + 1 : 1;
                    if (!plan.IsEmpty || plan.BaseVerdict != ProjectRecordBaseVerdict.Fresh) failures.Add(kind + " " + export.Guid + " " + plan.BaseVerdict + " changes " + plan.ChangeSet.Changes.Count + " refusals " + plan.ChangeSet.Refusals.Count);
                }
                _output.WriteLine(kind + ": " + count + " records planned");
            }
            _output.WriteLine("records " + total + ", changes " + changes + ", refusals " + refusals + ", base not fresh " + stale);
            foreach (var pair in refusalKinds.OrderBy(p => p.Key)) _output.WriteLine("  refusal " + pair.Key + " x " + pair.Value);
            foreach (var f in failures.Take(20)) _output.WriteLine("  " + f);
            Assert.True(failures.Count == 0, failures.Count + " of " + total + " records did not plan empty; see the output lines above (guids and counts only)");
        }
    }
}
