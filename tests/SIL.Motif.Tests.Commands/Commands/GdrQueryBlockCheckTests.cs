using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The Guide check's <c>motif-query</c> part: each block is run read-only through the app's evaluator and comes back as ok,
/// refused with its code, not built or failed, with a row count and elapsed time, and a problem is an advisory finding.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrQueryBlockCheckTests(PristineProjectFixture pristine)
{
    private static string Block(string id, string run) => $"```motif-query\nid: {id}\ntitle: {id}\nrun: {run}\n```";

    private static GdrQueryBlockSource Source(string id, string run, params GuideBlockDiagnostic[] diagnostics)
    {
        var parsed = GuideQueryBlocks.Parse(Block(id, run));
        return new GdrQueryBlockSource("GDR-1", "Forms", Assert.Single(parsed.Queries), diagnostics);
    }

    private const string Corpus = "motif query corpus forms --corpus words.tsv --grammar grammar.json forms where 'readings > 0'";

    private sealed record Indexed(GuideEvidenceRunnerTests.Recorded Builder, string CacheRoot)
    {
        public int Builds => Builder.Builds;
    }

    // A corpus, a grammar file and a recorded index in the folder; warm means the CLI run has built it.
    private static Indexed RecordedIndex(string folder, bool capped, bool warm = true)
    {
        File.WriteAllText(Path.Combine(folder, "words.tsv"), "solo\t10\npair\t5\nnone\t2\n");
        File.WriteAllText(Path.Combine(folder, "grammar.json"), "grammar");
        var builder = new GuideEvidenceRunnerTests.Recorded(capped ? GuideEvidenceRunnerTests.Capped : GuideEvidenceRunnerTests.Complete);
        var cacheRoot = Path.Combine(folder, "cache");
        if (warm)
        {
            Assert.True(QueryRunLine.TryParseRun(Corpus, out var parsed, out _));
            var request = SIL.Motif.Contract.Query.Corpus.CorpusRunLine.Resolve(parsed.Corpus!, folder);
            Assert.True(CorpusQueryCommand.Forms(request, null, default, builder, cacheRoot).Succeeded);
        }
        return new Indexed(builder, cacheRoot);
    }

    private static GuideEvidenceResult Ready(bool complete) =>
        new(GuideEvidenceStatus.Ready, null, "", null, null, 1, new Dictionary<string, double>());

    [Fact]
    public void AWarmCorpusBlockIsOkWithItsRowCountAndAnIncompleteParseSaysAtLeast()
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-warm").FullName;
        try
        {
            var runs = new List<GdrQueryBlockRun>();
            foreach (var capped in new[] { false, true })
            {
                var own = Directory.CreateDirectory(Path.Combine(folder, capped ? "capped" : "complete")).FullName;
                var recorded = RecordedIndex(own, capped);
                var result = GdrQueryBlockCheck.Check([Source("q", Corpus)], Path.Combine(own, "p.fwdata"), true, TimeSpan.FromSeconds(30), null,
                    (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot));
                runs.Add(Assert.Single(result.Runs));
                Assert.Empty(result.Findings);
            }
            Assert.Equal(GdrQueryBlockStatus.Ok, runs[0].Status);
            Assert.Equal((2L, true), (runs[0].Rows, runs[0].Complete));
            Assert.Equal(GdrQueryBlockStatus.Ok, runs[1].Status);
            Assert.False(runs[1].Complete);
            Assert.Equal(("GDR-1", "Forms", "q"), (runs[0].Gdr, runs[0].Section, runs[0].Id));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AColdIndexIsANotBuiltFindingThatNamesTheBuildCommandAndBuildsNothing()
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-cold").FullName;
        try
        {
            var recorded = RecordedIndex(folder, false, warm: false);
            var result = GdrQueryBlockCheck.Check([Source("q", Corpus)], Path.Combine(folder, "p.fwdata"), true, TimeSpan.FromSeconds(30), null,
                (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot));
            var block = Assert.Single(result.Runs);
            Assert.Equal(GdrQueryBlockStatus.NotBuilt, block.Status);
            Assert.Equal("query.parse-index-cold", block.Code);
            Assert.Equal(0, recorded.Builds);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(GdrSyncCodes.QueryNotBuilt, finding.Code);
            Assert.Contains("parse index not built: run cd '", finding.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ARefusedLineKeepsItsCodeAndDoesNotRun()
    {
        var invalid = GuideQueryBlocks.Parse(Block("w", "motif query corpus forms --corpus words.tsv --write-words forms"));
        var source = new GdrQueryBlockSource("GDR-1", null, Assert.Single(invalid.Queries), invalid.Diagnostics);
        var ran = false;
        var result = GdrQueryBlockCheck.Check([source], "p.fwdata", true, TimeSpan.FromSeconds(30), null,
            (_, _, _) => { ran = true; return Ready(true); });
        Assert.False(ran);
        var block = Assert.Single(result.Runs);
        Assert.Equal((GdrQueryBlockStatus.Refused, "guide-query.run-not-read-only"), (block.Status, block.Code));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(GdrSyncCodes.QueryRefused, finding.Code);
        Assert.Contains("guide-query.run-not-read-only", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoRunCheckListsBlocksAsSkippedWithNoFindingAndRunsNothing()
    {
        var ran = false;
        var result = GdrQueryBlockCheck.Check([Source("a", Corpus), Source("b", "motif query forms")], "p.fwdata", false, TimeSpan.FromSeconds(30), null,
            (_, _, _) => { ran = true; return Ready(true); });
        Assert.False(ran);
        Assert.All(result.Runs, block => Assert.Equal((GdrQueryBlockStatus.Skipped, 0d), (block.Status, block.Milliseconds)));
        Assert.Equal(["a", "b"], result.Runs.Select(block => block.Id));
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ABlockThatRunsPastTheTimeoutIsFailedAndTheNextBlockStillRuns()
    {
        var calls = 0;
        var result = GdrQueryBlockCheck.Check([Source("slow", Corpus), Source("next", Corpus)], "p.fwdata", true, TimeSpan.FromMilliseconds(200), null,
            (_, _, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1) token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                return new GuideEvidenceResult(GuideEvidenceStatus.Refused, null, "query.x: no", "query.x", null, 1, new Dictionary<string, double>());
            });
        Assert.Equal([GdrQueryBlockStatus.Failed, GdrQueryBlockStatus.Refused], result.Runs.Select(block => block.Status));
        Assert.Contains("timed out", result.Runs[0].Message, StringComparison.Ordinal);
        Assert.Equal([GdrSyncCodes.QueryFailed, GdrSyncCodes.QueryRefused], result.Findings.Select(finding => finding.Code));
    }

    [Fact]
    public void AnEvaluatorThatThrowsIsAFailedBlockNotACrash()
    {
        var result = GdrQueryBlockCheck.Check([Source("boom", Corpus)], "p.fwdata", true, TimeSpan.FromSeconds(30), null,
            (_, _, _) => throw new InvalidOperationException("kaput"));
        var block = Assert.Single(result.Runs);
        Assert.Equal(GdrQueryBlockStatus.Failed, block.Status);
        Assert.Contains("kaput", block.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupLineIsRefusedAndAV1QueryReadsTheBaselineThroughTheAppsEvaluator()
    {
        var result = GdrQueryBlockCheck.Check([Source("g", "motif group suffix s"), Source("v1", "motif query forms")],
            Path.Combine(Directory.CreateTempSubdirectory("gdr-check-v1").FullName, "none.fwdata"), true, TimeSpan.FromSeconds(30), null);
        Assert.Equal("guide-query.run-invalid", result.Runs[0].Code);
        Assert.Equal(GdrQueryBlockStatus.Refused, result.Runs[1].Status);
        Assert.NotNull(result.Runs[1].Code);
    }

    [Fact]
    public void AMalformedBlockIsAnAdvisoryFindingWithItsSectionAndWhenItHasNoIdItsOwnSource()
    {
        var parsed = GuideQueryBlocks.Parse("```motif-show\nquery: nowhere\n```");
        var result = GdrQueryBlockCheck.Check([new GdrQueryBlockSource("GDR-2", "Intro", null, parsed.Diagnostics)], "p.fwdata", true, TimeSpan.FromSeconds(30), null);
        Assert.Empty(result.Runs);
        var finding = Assert.Single(result.Findings);
        Assert.Equal((GdrSyncCodes.QueryBlockInvalid, "GDR-2", "Intro"), (finding.Code, finding.Gdr, finding.Section));
    }

    [Fact]
    public void TheCheckReadsTheBlocksOfTheBaselineGuideAndNoRunLeavesThemSkipped()
    {
        var path = ProjectWithBlocks(
            Block("refused", "motif query corpus forms --corpus words.tsv --write-words forms") + "\n\n" + Block("v1", "motif query forms") +
            "\n\n```motif-show\nquery: missing\n```");

        var ran = GdrQuery.Check(new GdrCheckRequest(path)).Value!;
        var skipped = GdrQuery.Check(new GdrCheckRequest(path, RunBlocks: false)).Value!;

        Assert.Equal(["refused", "v1"], ran.QueryBlocks!.Select(block => block.Id));
        Assert.Equal(GdrQueryBlockStatus.Refused, ran.QueryBlocks![0].Status);
        Assert.All(ran.QueryBlocks!, block => Assert.Equal("GDR-9", block.Gdr));
        Assert.Contains(ran.Findings, finding => finding.Code == GdrSyncCodes.QueryBlockInvalid);
        Assert.False(ran.InSync);
        Assert.All(skipped.QueryBlocks!, block => Assert.Equal(GdrQueryBlockStatus.Skipped, block.Status));
        Assert.DoesNotContain(skipped.Findings, finding => finding.Code is GdrSyncCodes.QueryRefused or GdrSyncCodes.QueryFailed or GdrSyncCodes.QueryNotBuilt);
        Assert.Contains(skipped.Findings, finding => finding.Code == GdrSyncCodes.QueryBlockInvalid);
    }

    private string ProjectWithBlocks(string body)
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        SeededGdrs.SeedRecord(cache, "GDR-9", "Queries");
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "gdr-check-root");
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-9", "## Forms\n\n" + body));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(root));
        try
        {
            Assert.True(PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null), dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner).Succeeded);
        }
        finally { runner.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        return path;
    }
}
