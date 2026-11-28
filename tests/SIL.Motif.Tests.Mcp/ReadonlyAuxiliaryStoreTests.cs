using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ReadonlyAuxiliaryStoreTests(PristineProjectFixture pristine)
{
    [Fact]
    public void StoredTrialReportReadDoesNotRecreateAMissingUseLease()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var context = Context(fixture.Project, fixture.Root);
        var arguments = new ToolArgs(new JsonObject
        {
            ["proposal"] = fixture.Proposal, ["report"] = fixture.Report,
        }, ["proposal", "report"]);
        var before = TrialReviewTools.Read(context, fixture.Proposal, arguments);
        Assert.Null(before.Refusal);
        Assert.Equal("report", before.Value!["state"]!.GetValue<string>());
        var files = RemoveLeaseAndSnapshot(fixture.Project);

        var result = TrialReviewTools.Read(context, fixture.Proposal, arguments);

        Assert.NotNull(result.Refusal);
        AssertUnchanged(fixture.Project, files);
    }

    [Fact]
    public async Task StoredAssessmentPagesDoNotRecreateAMissingUseLease()
    {
        var projectPath = pristine.CopyProjectFile();
        SeedAssessments(projectPath);
        var tool = AgentTools.All.Single(item => item.Name == "motif_assess");
        var arguments = new JsonObject { ["assessment"] = "before", ["limit"] = 1 };
        var args = new ToolArgs(arguments, tool.ArgumentNames);
        var context = Context(projectPath, Path.GetDirectoryName(projectPath)!);
        var before = await tool.Run(context, args, CancellationToken.None);
        Assert.Null(before.Refusal);
        Assert.Single(before.Value!["words"]!.AsArray());
        var files = RemoveLeaseAndSnapshot(projectPath);

        var result = await tool.Run(context, args, CancellationToken.None);

        Assert.NotNull(result.Refusal);
        AssertUnchanged(projectPath, files);
    }

    [Fact]
    public void BaselineReadAndAuthoredRuleNamesDoNotRecreateAMissingUseLease()
    {
        using var project = new WalkthroughProject(pristine);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        using var readers = new BaselineReadSession(path => BaselineReadCache.Open(path));
        var before = BaselineReads.Read(project.FwDataPath,
            cache => new JsonObject { ["entries"] = cache.ServiceLocator.GetInstance<ILexEntryRepository>().Count }, readers);
        Assert.True(before.Succeeded, before.Refusal?.Message);
        Assert.True(before.Value!["entries"]!.GetValue<int>() > 0);
        var trace = FailedTrace(project.FirstMsaId);
        var named = TryWordRuleNames.Named(project.FwDataPath, trace);
        Assert.NotNull(Assert.Single(named.Reading.Refs).CapturedFieldWorksLabel);
        var files = RemoveLeaseAndSnapshot(project.FwDataPath);
        var readProject = false;

        var result = BaselineReads.Read(project.FwDataPath, _ =>
        {
            readProject = true;
            return new JsonObject();
        }, readers);
        var unchangedTrace = TryWordRuleNames.Named(project.FwDataPath, trace);

        Assert.False(result.Succeeded);
        Assert.False(readProject);
        Assert.Same(trace, unchangedTrace);
        AssertUnchanged(project.FwDataPath, files);
    }

    private static void SeedAssessments(string projectPath)
    {
        var words = new[] { "first" };
        AssessedWord[] before = [new("first", "analysed",
            [new ParsedAnalysis(null, ["old-morph"], 0, "old-identity")])];
        AssessedWord[] after = [new("first", "no-analysis", [])];
        var stored = ProjectStoreCommand.Run(projectPath, "1.0", (database, _) =>
        {
            NewAssessmentRecord Record(string id, string kind, string scopeJson, IReadOnlyList<AssessedWord> rows) =>
                new(id, null, null, "fixture", kind, scopeJson, "scope",
                    new AssessmentSubject.Words(Selection.Create("fixture", words), "none", "1"),
                    "{}", "outcome", "semantic", "grammar", "model", "trial-difference/v1", 0,
                    new AssessmentRecordMaterial.Words(rows, null, null, []));
            new AssessmentRepository(database).RecordBatch([Record("before", "Correctness", "{}", before),
                Record("after", "Correctness", "{}", after)]);
            return CommandOutcome<JsonObject>.Success(new JsonObject());
        });
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
    }

    private static WordTraceResponse FailedTrace(Guid rule)
    {
        var step = new TraceStep("Failed", null, null, null, "PartialParse", []);
        return new WordTraceResponse("unparsed", false, true, null, 0, "mrule#1", 0,
            new WordTraceReading("unparsed", step, [], [], [], [], [])
            {
                Refs = [new TraceRef("rule", "morphRule", "mrule#1")
                    { Identity = rule.ToString("D"), IdentityQuality = "authored" }],
            });
    }

    private static ServerContext Context(string projectPath, string root) => new(projectPath, "1.0",
        new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)), new ActivityLog(), ToolProfile.Builtin,
        TextWriter.Null, advancedAiModeEnabled: true);

    private static Dictionary<string, byte[]> RemoveLeaseAndSnapshot(string projectPath)
    {
        var lease = Path.ChangeExtension(projectPath, ".motif.db") + ".use.lock";
        Assert.True(File.Exists(lease));
        File.Delete(lease);
        return Snapshot(projectPath);
    }

    private static Dictionary<string, byte[]> Snapshot(string projectPath) => Directory
        .EnumerateFiles(Path.GetDirectoryName(projectPath)!, "*", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith("-wal", StringComparison.Ordinal) && !path.EndsWith("-shm", StringComparison.Ordinal))
        .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertUnchanged(string projectPath, IReadOnlyDictionary<string, byte[]> before)
    {
        var after = Snapshot(projectPath);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
