using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class DecisionEvidenceToolTests
{
    private const string Tool = "motif_decision_evidence";

    static DecisionEvidenceToolTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(WfiAnalysisOperationKinds).Module.ModuleHandle);

    private static string ShippedProfile() => Path.Combine(AppContext.BaseDirectory, "profiles", "source-validated.json");

    [Fact]
    public void TheToolIsOptInAbsentFromTheDefaultSurfaceAndPresentInItsOwnProfile()
    {
        var defaultNames = MotifMcpServer.Expose(ToolProfile.Builtin).Select(tool => tool.Name).ToArray();
        var optIn = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile()));

        Assert.DoesNotContain(Tool, defaultNames);
        Assert.Contains(Tool, optIn.Select(tool => tool.Name));
    }

    [Fact]
    public void ItsTierChoicesAreTheClosedVocabulary()
    {
        var tool = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile())).Single(candidate => candidate.Name == Tool);
        var properties = (System.Text.Json.Nodes.JsonObject)tool.Tool.InputSchema["properties"]!;

        var tiers = ((System.Text.Json.Nodes.JsonArray)properties["tier"]!["enum"]!).Select(node => node!.GetValue<string>());

        Assert.Equal(DecisionEvidence.Tiers, tiers);
    }

    [Fact]
    public async Task APopulatedPagedReadMatchesTheCommandsJsonForListAndShow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-decision-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var privateRoot = PrivateManagedRoot.Use(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            var subjects = new List<string>();
            foreach (var tier in new[] { "undecided", "ill-formed", "ill-formed", "undecided", "ill-formed" })
                subjects.Add(Stage(project, tier));
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Load(ShippedProfile()), TextWriter.Null, advancedAiModeEnabled: true);
            var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile()));

            var page = await Call(context, tools, project, ("offset", 1), ("limit", 2));
            var filtered = await Call(context, tools, project, ("tier", "ill-formed"));
            var shown = await Call(context, tools, project, ("subject", subjects[1]));

            AssertSameJson(ProposalCommands.EvidenceList(new EvidenceListRequest(project, "1.0", null, 1, 2)).Value!, page);
            Assert.Equal(5, page.GetProperty("total").GetInt32());
            Assert.Equal(2, page.GetProperty("rows").GetArrayLength());
            Assert.Equal(3, page.GetProperty("tierCounts").GetProperty("ill-formed").GetInt32());
            AssertSameJson(ProposalCommands.EvidenceList(new EvidenceListRequest(project, "1.0", "ill-formed")).Value!, filtered);
            Assert.Equal(3, filtered.GetProperty("total").GetInt32());
            AssertSameJson(ProposalCommands.EvidenceShow(new EvidenceShowRequest(project, "1.0", subjects[1])).Value!, shown);
            Assert.Equal("source-validated", shown.GetProperty("records")[0].GetProperty("label").GetString());
            Assert.False(shown.GetProperty("records")[0].GetProperty("human").GetBoolean());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AnUnknownTierIsRefusedWithItsCode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-decision-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var privateRoot = PrivateManagedRoot.Use(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Load(ShippedProfile()), TextWriter.Null, advancedAiModeEnabled: true);

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile())), Tool,
                new Dictionary<string, JsonElement>
                {
                    ["project"] = JsonSerializer.SerializeToElement(project),
                    ["tier"] = JsonSerializer.SerializeToElement("gold"),
                }, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("evidence.unknown-tier", result.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TheExportToolPagesThroughACursorAndMatchesTheCommandsJsonAndRefusesABadCursor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-decision-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var privateRoot = PrivateManagedRoot.Use(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            foreach (var tier in new[] { "undecided", "ill-formed", "ill-formed" }) Stage(project, tier);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Load(ShippedProfile()), TextWriter.Null, advancedAiModeEnabled: true);
            var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile()));
            const string export = "motif_evidence_export";

            async Task<JsonElement> Page(params (string Key, object Value)[] arguments)
            {
                var map = new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(project) };
                foreach (var (key, value) in arguments) map[key] = JsonSerializer.SerializeToElement(value);
                var result = await MotifMcpServer.CallAsync(context, tools, export, map, CancellationToken.None);
                Assert.NotEqual(true, result.IsError);
                return result.StructuredContent!.Value.GetProperty("result");
            }

            var schema = (System.Text.Json.Nodes.JsonObject)tools.Single(t => t.Name == export).Tool.InputSchema["properties"]!["limit"]!;
            Assert.Equal((1, 500, 50), (schema["minimum"]!.GetValue<int>(), schema["maximum"]!.GetValue<int>(),
                schema["default"]!.GetValue<int>()));
            var first = await Page(("limit", 2));
            var second = await Page(("limit", 2), ("after", first.GetProperty("next").GetString()!));

            AssertSameJson(ProposalCommands.EvidenceExport(new EvidenceExportRequest(project, "1.0", null, null, 2)).Value!, first);
            Assert.Equal(2, first.GetProperty("rows").GetArrayLength());
            Assert.Equal(1, second.GetProperty("rows").GetArrayLength());
            Assert.False(second.TryGetProperty("next", out _));
            Assert.Equal("motif-decision-evidence/1", first.GetProperty("format").GetString());
            Assert.Equal("source-validated", first.GetProperty("rows")[0].GetProperty("label").GetString());

            var explicitMaximum = await Page(("limit", 500));
            Assert.Equal(3, explicitMaximum.GetProperty("rows").GetArrayLength());
            var tooMany = await MotifMcpServer.CallAsync(context, tools, export, new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["limit"] = JsonSerializer.SerializeToElement(501),
            }, CancellationToken.None);
            Assert.True(tooMany.IsError);
            Assert.Equal("tool.invalid-argument", tooMany.StructuredContent!.Value.GetProperty("code").GetString());

            var refused = await MotifMcpServer.CallAsync(context, tools, export, new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["after"] = JsonSerializer.SerializeToElement("nonsense"),
            }, CancellationToken.None);
            Assert.True(refused.IsError);
            Assert.Equal("evidence.invalid-cursor", refused.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AnOmittedExportLimitReturnsFiftyRowsAndACursor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-decision-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var privateRoot = PrivateManagedRoot.Use(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            for (var index = 0; index < 52; index++) Stage(project, "undecided");
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Load(ShippedProfile()), TextWriter.Null, advancedAiModeEnabled: true);
            var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile()));

            var result = await MotifMcpServer.CallAsync(context, tools, "motif_evidence_export",
                new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(project) },
                CancellationToken.None);

            Assert.NotEqual(true, result.IsError);
            var page = result.StructuredContent!.Value.GetProperty("result");
            Assert.Equal(50, page.GetProperty("rows").GetArrayLength());
            Assert.True(page.TryGetProperty("next", out _));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TheListToolDeclaresAndHonoursItsFullPageRange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-decision-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var privateRoot = PrivateManagedRoot.Use(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            Stage(project, "undecided");
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Load(ShippedProfile()), TextWriter.Null, advancedAiModeEnabled: true);
            var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile()));
            var limit = (System.Text.Json.Nodes.JsonObject)tools.Single(t => t.Name == Tool).Tool.InputSchema["properties"]!["limit"]!;

            var aboveTheDefaultWindow = await Call(context, tools, project, ("limit", 300));
            var tooMany = await MotifMcpServer.CallAsync(context, tools, Tool, new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["limit"] = JsonSerializer.SerializeToElement(501),
            }, CancellationToken.None);

            Assert.Equal((1, 500), (limit["minimum"]!.GetValue<int>(), limit["maximum"]!.GetValue<int>()));
            Assert.Equal(1, aboveTheDefaultWindow.GetProperty("total").GetInt32());
            Assert.True(tooMany.IsError);
            Assert.Equal("tool.invalid-argument", tooMany.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<JsonElement> Call(ServerContext context, IReadOnlyList<ExposedTool> tools, string project,
        params (string Key, object Value)[] arguments)
    {
        var map = new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(project) };
        foreach (var (key, value) in arguments) map[key] = JsonSerializer.SerializeToElement(value);
        var result = await MotifMcpServer.CallAsync(context, tools, Tool, map, CancellationToken.None);
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value.GetProperty("result");
    }

    private static void AssertSameJson(object expected, JsonElement actual)
    {
        using var parsed = JsonDocument.Parse(ProjectionJson.Serialize(expected));
        Assert.Equal(CanonicalJson.Canonicalize(parsed.RootElement.GetRawText()),
            CanonicalJson.Canonicalize(actual.GetRawText()));
    }

    private static string Stage(string project, string tier)
    {
        var subject = CanonicalId.Mint();
        var proposalId = CanonicalId.Mint();
        var json = "{\"contractVersions\":{\"analysis\":\"1.0\"},\"proposalId\":\"" + proposalId.Value +
            "\",\"requires\":[],\"operations\":[{\"operationId\":\"" + CanonicalId.Mint().Value +
            "\",\"kind\":\"analysis/decisionEvidence/record\",\"target\":\"" + subject.Value +
            "\",\"after\":{},\"provenance\":[{\"type\":\"decisionEvidence\",\"tier\":\"" + tier +
            "\",\"sources\":[],\"detail\":{\"violatedRule\":\"class prefix\"}}]}]}";
        var proposal = ProposalJsonParser.Parse(json);
        var outcome = ProjectStoreCommand.Run(project, "1.0", (database, _) =>
        {
            var repository = new ProposalRepository(database);
            repository.SaveRevision(new ProposalRevisionRecord(
                proposalId, IntentDigest.Compute(proposal), json, "proposed", null, null, null));
            repository.SetStatus(proposalId, "applied", null);
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return subject.Value;
    }
}
