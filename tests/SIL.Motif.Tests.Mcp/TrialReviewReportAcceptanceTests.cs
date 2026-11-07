using System.Text.Json;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class TrialReviewReportAcceptanceTests
{
    [Fact]
    public async Task EveryDisclosureLevelKeepsSameFactsAndReachesEveryCase()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-acceptance-machine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var managed = PrivateManagedRoot.Use(root);
            using var fixture = new ReviewAcceptanceFixture();
            var managedRoot = RunnerOptions.ResolveRoot();
            using (var machine = MachineDatabase.Open(managedRoot))
                new KnownProjectRegistry(machine).Record(fixture.ProjectKey, fixture.Project, DateTimeOffset.UtcNow);
            var context = new ServerContext(fixture.Project, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, null)),
                new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            async Task<JsonElement> Read(string detail, string? cursor = null)
            {
                var args = new Dictionary<string, JsonElement> {
                    ["project"] = JsonSerializer.SerializeToElement(fixture.Project), ["proposal"] = JsonSerializer.SerializeToElement(fixture.Proposal), ["action"] = JsonSerializer.SerializeToElement("read"),
                    ["report"] = JsonSerializer.SerializeToElement(fixture.Report), ["detail"] = JsonSerializer.SerializeToElement(detail) };
                if (detail == "full") args["limit"] = JsonSerializer.SerializeToElement(1);
                if (cursor is not null) args["cursor"] = JsonSerializer.SerializeToElement(cursor);
                var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_trial", args, CancellationToken.None);
                Assert.NotEqual(true, result.IsError);
                return result.StructuredContent!.Value.GetProperty("result").GetProperty("report").Clone();
            }
            var summary = await Read("summary");
            Assert.True(JsonElement.DeepEquals(summary.GetProperty("summary"), (await Read("failures")).GetProperty("summary")));
            var keys = new List<string>();
            string? cursor = null;
            do
            {
                var report = await Read("full", cursor);
                Assert.True(JsonElement.DeepEquals(summary.GetProperty("summary"), report.GetProperty("summary")));
                var page = report.GetProperty("page");
                keys.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("caseKey").GetString()!));
                cursor = page.GetProperty("nextCursor").GetString();
            } while (cursor is not null);
            Assert.Equal(new[] { "approved", "homograph/a", "homograph/b", "incomplete", "negative", "reference", "unchanged" }, keys.Order().ToArray());
            var prefix = $"motif://projects/{Uri.EscapeDataString(fixture.ProjectKey)}/trial-reports/{Uri.EscapeDataString(fixture.Report)}";
            var resource = ReviewReportResources.Read(prefix + "/sections/wordforms?limit=1", "1.0", managedRoot);
            var content = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
            Assert.Single(JsonDocument.Parse(content.Text!).RootElement.GetProperty("items").EnumerateArray());
            foreach (var section in new[] { "controls", "negative-controls", "incomplete", "sampled-tokens", "paradigm-cells", "warnings", "parsimony", "evidence" })
            {
                cursor = null;
                long returned = 0;
                long? expected = null;
                var cursors = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var uri = prefix + "/sections/" + section + "?limit=1" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                    var response = ReviewReportResources.Read(uri, "1.0", managedRoot);
                    var page = JsonDocument.Parse(Assert.IsType<TextResourceContents>(Assert.Single(response.Contents)).Text!).RootElement;
                    returned += page.GetProperty("returned").GetInt64();
                    if (page.GetProperty("totalMatching").ValueKind == JsonValueKind.Number)
                        expected = page.GetProperty("totalMatching").GetInt64();
                    cursor = page.GetProperty("nextCursor").GetString();
                    if (cursor is not null) Assert.True(cursors.Add(cursor));
                } while (cursor is not null);
                if (expected is not null) Assert.Equal(expected.Value, returned);
            }
            foreach (var key in keys)
            foreach (var kind in new[] { "memberships", "expectations", "sources", "readings" })
            {
                cursor = null;
                long returned = 0;
                long? expected = null;
                var cursors = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var uri = prefix + "/cases/" + Uri.EscapeDataString(key) + "/" + kind + "?limit=1" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                    var response = ReviewReportResources.Read(uri, "1.0", managedRoot);
                    var page = JsonDocument.Parse(Assert.IsType<TextResourceContents>(Assert.Single(response.Contents)).Text!).RootElement;
                    returned += page.GetProperty("returned").GetInt64();
                    if (page.GetProperty("totalMatching").ValueKind == JsonValueKind.Number)
                        expected = page.GetProperty("totalMatching").GetInt64();
                    cursor = page.GetProperty("nextCursor").GetString();
                    if (cursor is not null) Assert.True(cursors.Add(cursor));
                } while (cursor is not null);
                if (expected is not null) Assert.Equal(expected.Value, returned);
            }
            var readingCount = 0;
            cursor = null;
            do
            {
                var uri = prefix + "/cases/approved/readings?limit=1" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                var child = ReviewReportResources.Read(uri, "1.0", managedRoot);
                var page = JsonDocument.Parse(Assert.IsType<TextResourceContents>(Assert.Single(child.Contents)).Text!).RootElement;
                readingCount += page.GetProperty("items").GetArrayLength();
                cursor = page.GetProperty("nextCursor").GetString();
            } while (cursor is not null);
            Assert.True(readingCount >= 3);
            var args = new Dictionary<string, JsonElement> {
                ["project"] = JsonSerializer.SerializeToElement(fixture.Project), ["proposal"] = JsonSerializer.SerializeToElement(fixture.Proposal),
                ["report"] = JsonSerializer.SerializeToElement(fixture.Report), ["detail"] = JsonSerializer.SerializeToElement("full"),
                ["case"] = JsonSerializer.SerializeToElement("approved"), ["section"] = JsonSerializer.SerializeToElement("readings"),
                ["side"] = JsonSerializer.SerializeToElement("before"), ["limit"] = JsonSerializer.SerializeToElement(1) };
            var first = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_difference", args, CancellationToken.None);
            Assert.NotEqual(true, first.IsError);
            var next = first.StructuredContent!.Value.GetProperty("result").GetProperty("report").GetProperty("page").GetProperty("nextCursor").GetString();
            Assert.NotNull(next);
            args["cursor"] = JsonSerializer.SerializeToElement(next);
            args["side"] = JsonSerializer.SerializeToElement("after");
            var invalid = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_difference", args, CancellationToken.None);
            Assert.True(invalid.IsError);
            Assert.Equal("review.cursor-invalid", invalid.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void PublicRecursiveOutputsHaveNoRawPerformanceFields()
    {
        using var payload = JsonDocument.Parse("{\"parserWork\":12345}");
        var step = new TraceStep("failure", "rule", null, null, "NoRoot", [])
        {
            FailureEvidence = new TraceFailureEvidence(null, null, "NoRoot", "available", null, null, null, null, null)
                { Payload = payload.RootElement.Clone() },
        };
        var morph = new TraceMorph(null, "word", null, "gloss", null, null, null, null, null, null)
            { RawJson = "{\"parserWork\":12345}" };
        var reading = new WordTraceReading("word", step, [], [new TraceAnalysis("a", 0, "word", "available", [morph])], [], [], []);
        var trace = new WordTraceResponse("word", false, false, "limit", 200, "rule", 123, reading)
        {
            ParserWork = 12345, ParserElapsedMs = 42, SearchStatus = "incomplete",
            DiagnosticJson = "{\"nested\":{\"parserWork\":12345}}",
            Effort = [new TraceEffort("rule", 1, 2, 3, 4, 5, 6, 7) { Work = 12345 }],
        };
        var outputs = new[] { TryWordSummary.Concise(trace).ToJsonString(),
            ProjectionJson.Serialize(StandaloneTraceProjection.From(trace)),
            ProjectionJson.Serialize(StandaloneTraceProjection.From(trace with { Lowercase = trace })) };
        foreach (var output in outputs)
        {
            using var document = JsonDocument.Parse(output);
            Check(document.RootElement);
        }

        static void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    Assert.DoesNotContain(property.Name, new[] { "parserWork", "elapsedMs", "selfMs", "diagnosticJson", "rawJson" }, StringComparer.OrdinalIgnoreCase);
                    Check(property.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var child in element.EnumerateArray()) Check(child);
            else if (element.ValueKind == JsonValueKind.Number) Assert.NotEqual("12345", element.GetRawText());
        }
    }

    [Fact]
    public void ThereIsNoParityVerdictOrApplyTool()
    {
        var names = MotifMcpServer.Expose(ToolProfile.Builtin).Select(item => item.Tool.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("apply", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("parity", StringComparison.OrdinalIgnoreCase));
    }
}
