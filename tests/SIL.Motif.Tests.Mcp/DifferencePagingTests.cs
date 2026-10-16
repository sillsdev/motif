using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Mcp;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class DifferencePagingTests
{
    [Fact]
    public async Task UnknownDifferenceIsReportedAsNotFoundInsteadOfDamagedStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-difference-not-found-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["difference"] = JsonSerializer.SerializeToElement("assessment/not-found"),
                ["category"] = JsonSerializer.SerializeToElement("other-changes"),
            };

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                "motif_difference", arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("tool.not-found", result.StructuredContent!.Value.GetProperty("code").GetString());
            Assert.Equal("NotFound", result.StructuredContent.Value.GetProperty("reason").GetString());
            Assert.DoesNotContain("damaged", ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text,
                StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PagesKeepTheExactBeforeAndAfterAnalysesAndDoNotMixCategories()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-difference-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            var words = new[] { "first", "second", "unfinished" };
            var before = words.Select(word => new AssessedWord(word, "analysed",
                [new ParsedAnalysis(null, ["old-morph"], 0, "old-identity")])).ToArray();
            var after = words.Select(word => new AssessedWord(word, word == "unfinished" ? "incomplete" : "no-analysis", [])
                { IsIncomplete = word == "unfinished" }).ToArray();
            var scope = ScopeCodec.Write(new StoredScope.Difference("before", "after", 3, 3, 3,
                "old-grammar", "new-grammar", false, null));
            var recorded = ProjectStoreCommand.Run(project, "1.0", (database, _) =>
            {
                var assessments = new AssessmentRepository(database);
                NewAssessmentRecord Record(string id, string kind, string scopeJson, IReadOnlyList<AssessedWord> rows) =>
                    new(id, null, null, "fixture", kind, scopeJson, "scope",
                        new SIL.Motif.Host.Assess.AssessmentSubject.Words(Selection.Create("fixture", words), "none", "1"),
                        "{}", "outcome", "semantic", "grammar", "model",
                        "trial-difference/v1", 0, new AssessmentRecordMaterial.Words(rows, null, null, []));
                assessments.RecordBatch([Record("before", "Correctness", "{}", before),
                    Record("after", "Correctness", "{}", after),
                    Record("difference", "Difference", scope, TrialResults.Classify(before, after, []))]);
                return CommandOutcome<JsonObject>.Success(new JsonObject());
            });
            Assert.True(recorded.Succeeded, recorded.Refusal?.Message);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)),
                new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            JsonObject Page(string category, int offset) => Assert.IsType<JsonObject>(TrialResults.Read(context,
                new ToolArgs(new JsonObject { ["difference"] = "difference", ["category"] = category,
                    ["offset"] = offset, ["limit"] = 1 }, ["difference", "category", "offset", "limit"])).Value);
            var first = Page("positives-lost", 0);
            Assert.Equal(2, first["total"]!.GetValue<int>());
            Assert.Equal(1, first["nextOffset"]!.GetValue<int>());
            Assert.Equal("first", first["items"]![0]!["word"]!.GetValue<string>());
            Assert.Equal("old-identity", first["items"]![0]!["before"]!["analyses"]![0]!["identityDigest"]!.GetValue<string>());
            Assert.Empty(first["items"]![0]!["after"]!["analyses"]!.AsArray());
            var second = Page("positives-lost", first["nextOffset"]!.GetValue<int>());
            Assert.Equal("second", second["items"]![0]!["word"]!.GetValue<string>());
            Assert.Null(second["nextOffset"]);
            Assert.Equal("unfinished", Page("unfinished", 0)["items"]![0]!["word"]!.GetValue<string>());
        }
        finally { Directory.Delete(root, true); }
    }
}
