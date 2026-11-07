using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class TrialReviewResourceTests
{
    [Fact]
    public async Task ReportAndLegacyDifferenceSelectorsAreDisjointAndTraceIsEffectful()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_difference");
        Assert.False(tool.ReadOnly);
        Assert.False(tool.Idempotent);
        Assert.NotNull(tool.OutputSchema);
        var args = new ToolArgs(new System.Text.Json.Nodes.JsonObject
        {
            ["proposal"] = "proposal/test", ["report"] = "report/test", ["difference"] = "legacy/test"
        }, tool.ArgumentNames);
        var result = await tool.Run(null!, args, CancellationToken.None);
        Assert.Equal("tool.invalid-argument", result.Refusal!.Code);
    }

    [Fact]
    public void AdvertisedReportSectionCaseAndChildrenCanBeReadWithoutAParser()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-review-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var managed = PrivateManagedRoot.Use(root);
            var path = Path.Combine(root, "project.fwdata");
            File.WriteAllText(path, "");
            var project = new ProjectLocator(path, "project");
            var key = ProjectWorkspaceKey.Compute(project);
            using (var machine = MachineDatabase.Open(root)) new KnownProjectRegistry(machine).Record(key, path, DateTimeOffset.UtcNow);
            var outcome = ProjectStoreCommand.Run<string>(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Proposals (ProposalId,CurrentIntentDigest,Status) VALUES ('proposal/test','test','proposed');";
                command.ExecuteNonQuery();
                var report = (ReportView)Create(typeof(ReportView));
                report = report with { Schema = "motif.proposal-review-report", SchemaVersion = 1,
                    Pins = report.Pins with { ProjectKey = key, ProposalId = "proposal/test", TrialJobId = "job/test" },
                    Summary = report.Summary with { Notice = "No red flags means only 'not bad', never 'good'.",
                        Incomplete = report.Summary.Incomplete with { Label = "Incomplete" } },
                    FlagEvidence = Enum.GetValues<ReviewFlagId>().Select(flag => new FlagEvidence(flag, EvidenceState.Unavailable, [], 0)).ToArray() };
                var publication = new ReviewReportPublication(key, "proposal/test", "job/test", "manifest", report, [], 10000);
                var id = new ReviewReportRepository(database).PublishAsync(publication, Cases()).GetAwaiter().GetResult();
                ReviewTraceArtifacts.Store(database, new RetainedReviewTrace(new string('a', 64), key, id.Value, "case-a", Side.After,
                    "parser", "grammar", "scope", "complete", true, [new TraceNode("0", null, "rule", "test rule", null, [], null),
                        new TraceNode("1", "0", "rule", "child rule", null, [], null)]));
                return CommandOutcome<string>.Success(id.Value);
            });
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            var prefix = $"motif://projects/{Uri.EscapeDataString(key)}/trial-reports/{Uri.EscapeDataString(outcome.Value!)}";
            var summary = Read(prefix, root);
            Assert.Equal("report", summary.GetProperty("state").GetString());
            var page = Read(prefix + "/sections/wordforms?limit=1", root);
            Assert.Single(page.GetProperty("items").EnumerateArray());
            var direct = ProjectStoreCommand.Run<ReviewPage>(path, "1.0", (database, _) =>
                CommandOutcome<ReviewPage>.Success(new ReviewReportRepository(database).ReadPage(
                    new ReviewPageRequest(key, new ReviewReportId(outcome.Value!), "wordforms", Limit: 1))));
            Assert.True(direct.Succeeded, direct.Refusal?.Message);
            Assert.True(JsonElement.DeepEquals(page, JsonSerializer.SerializeToElement(direct.Value, ReviewJson.Options)));
            var cursor = page.GetProperty("nextCursor").GetString();
            Assert.NotNull(cursor);
            var second = Read(prefix + "/sections/wordforms?limit=1&cursor=" + Uri.EscapeDataString(cursor), root);
            Assert.Single(second.GetProperty("items").EnumerateArray());
            Assert.NotEqual(page.GetProperty("items")[0].GetProperty("caseKey").GetString(),
                second.GetProperty("items")[0].GetProperty("caseKey").GetString());
            Assert.Throws<McpProtocolException>(() => ReviewReportResources.Read(prefix + "/cases/case-a", "1.0", root, allowDrillDown: false));
            Assert.Equal(2, ReviewReportResources.Templates(allowDrillDown: false).ResourceTemplates.Count);
            var word = Read(prefix + "/cases/case-a", root);
            Assert.Equal("case-a", word.GetProperty("row").GetProperty("caseKey").GetString());
            Assert.Equal("motif_difference", word.GetProperty("traceRequest").GetProperty("tool").GetString());
            var memberships = Read(prefix + "/cases/case-a/memberships", root);
            Assert.Equal(JsonValueKind.Array, memberships.GetProperty("items").ValueKind);
            var trace = Read(prefix + "/traces/" + new string('a', 64), root);
            Assert.Equal("stored", trace.GetProperty("origin").GetString());
            Assert.Equal("test rule", trace.GetProperty("nodes")[0].GetProperty("label").GetString());
            var childUri = trace.GetProperty("nodes")[0].GetProperty("children").GetProperty("uri").GetString();
            Assert.NotNull(childUri);
            Assert.Equal("child rule", Read(childUri, root).GetProperty("nodes")[0].GetProperty("label").GetString());
            Assert.Throws<McpProtocolException>(() => ReviewReportResources.Read(prefix + "/traces/" + new string('b', 64), "1.0", root));
            Assert.Throws<McpProtocolException>(() => ReviewReportResources.Read(prefix.Replace(Uri.EscapeDataString(key), "other"), "1.0", root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static JsonElement Read(string uri, string root)
    {
        var result = ReviewReportResources.Read(uri, "1.0", root);
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("application/json", content.MimeType);
        return JsonDocument.Parse(content.Text!).RootElement.Clone();
    }

    private static async IAsyncEnumerable<ReviewCaseDelta> Cases()
    {
        yield return new ReviewCaseDelta("case-a", "word-a", "word", "ws", false, false, false, [], [], [], []);
        yield return new ReviewCaseDelta("case-b", "word-b", "word2", "ws", false, false, false, [], [], [], []);
        await Task.CompletedTask;
    }

    private static object Create(Type type)
    {
        if (type == typeof(BaselineToken)) return new BaselineToken("project", "sha256:" + new string('a', 64), "v1", "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64));
        if (type == typeof(string)) return "test";
        if (type == typeof(long)) return 1L;
        if (type == typeof(int)) return 1;
        if (type == typeof(double)) return 1d;
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)) return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type == typeof(ReadTarget)) return new ReadTarget(TargetKind.Resource, "test", "motif://test", null, null);
        if (type == typeof(EvidencePin)) return Create(typeof(SetEvidencePin));
        if (type == typeof(FlagFacts) || type == typeof(WorkChange)) return new WorkChange(null, 0, "test", WorkComparison.NotComparable, []);
        return type.GetConstructors().Single().Invoke(type.GetConstructors().Single().GetParameters().Select(parameter =>
            Nullable.GetUnderlyingType(parameter.ParameterType) is not null || new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable
                ? null : Create(parameter.ParameterType)).ToArray());
    }
}
