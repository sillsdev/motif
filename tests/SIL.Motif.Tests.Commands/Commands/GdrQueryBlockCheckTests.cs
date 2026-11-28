using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
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
    public void ABlockWhoseRunLineIsNotNfcIsFlaggedAndAnNfcBlockIsNot()
    {
        var composed = Corpus.Replace("readings > 0", "form ~ \\\"\u0B95\u0BCB\\\"", StringComparison.Ordinal);
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        Assert.NotEqual(composed, decomposed);
        var result = GdrQueryBlockCheck.Check([Source("nfc", composed), Source("nfd", decomposed)], "p.fwdata", false, TimeSpan.FromSeconds(5), null);
        var finding = Assert.Single(result.Findings, f => f.Code == GdrSyncCodes.QueryNotNormalized);
        Assert.Equal(["nfd"], finding.Subjects);
    }

    private static GdrQueryBlockSource Expecting(string expect)
    {
        var parsed = GuideQueryBlocks.Parse($"```motif-query\nid: q\ntitle: q\nrun: {Corpus}\nexpect: {expect}\n```");
        return new GdrQueryBlockSource("GDR-1", "Forms", Assert.Single(parsed.Queries), parsed.Diagnostics);
    }

    [Theory]
    [InlineData("2", false)]
    [InlineData("1..3", false)]
    [InlineData(">=1", false)]
    [InlineData("3", true)]
    [InlineData("5..9", true)]
    [InlineData(">2", true)]
    public void ACountOutsideTheExpectedCountIsATypedFindingWithExpectedAndActual(string expect, bool mismatch)
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-expect").FullName;
        try
        {
            var recorded = RecordedIndex(folder, false);
            var result = GdrQueryBlockCheck.Check([Expecting(expect)], Path.Combine(folder, "p.fwdata"), true, TimeSpan.FromSeconds(30), null,
                (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot));
            var run = Assert.Single(result.Runs);
            Assert.Equal((2L, expect), (run.Rows, run.Expected));
            if (!mismatch) { Assert.Empty(result.Findings); return; }
            var finding = Assert.Single(result.Findings);
            Assert.Equal(GdrSyncCodes.QueryCountMismatch, finding.Code);
            Assert.Contains($"expected {expect}", finding.Message, StringComparison.Ordinal);
            Assert.Contains("found 2", finding.Message, StringComparison.Ordinal);
            Assert.Equal(["q"], finding.Subjects);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("5", false)]       // at least 2 of a count that may still reach 5
    [InlineData("1..9", false)]
    [InlineData(">=1", false)]
    [InlineData("1", true)]        // at least 2 already passes the most allowed
    [InlineData("<2", true)]
    [InlineData("<=1", true)]
    public void AnIncompleteParseIsAMismatchOnlyWhenItsLowerBoundPassesTheMostExpected(string expect, bool mismatch)
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-expect-capped").FullName;
        try
        {
            var recorded = RecordedIndex(folder, true);
            var result = GdrQueryBlockCheck.Check([Expecting(expect)], Path.Combine(folder, "p.fwdata"), true, TimeSpan.FromSeconds(30), null,
                (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot));
            Assert.False(Assert.Single(result.Runs).Complete);
            if (!mismatch) { Assert.Empty(result.Findings); return; }
            var finding = Assert.Single(result.Findings);
            Assert.Equal(GdrSyncCodes.QueryCountMismatch, finding.Code);
            Assert.Contains("found at least 2", finding.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ARefusedOrFailedBlockWithAnExpectedCountGivesItsOwnFindingAndNoMismatch()
    {
        var refused = GdrQueryBlockCheck.Check([Expecting("5")], "p.fwdata", true, TimeSpan.FromSeconds(5), null,
            (_, _, _) => new GuideEvidenceResult(GuideEvidenceStatus.Refused, null, "no", "query.test", null, 0, new Dictionary<string, double>()));
        Assert.Equal(GdrSyncCodes.QueryRefused, Assert.Single(refused.Findings).Code);
        var failed = GdrQueryBlockCheck.Check([Expecting("5")], "p.fwdata", true, TimeSpan.FromSeconds(5), null,
            (_, _, _) => throw new InvalidOperationException("boom"));
        Assert.Equal(GdrSyncCodes.QueryFailed, Assert.Single(failed.Findings).Code);
    }

    [Fact]
    public void ABadExpectPassesThroughTheCheckAsAnInvalidBlockFindingAndTheBlockStillRuns()
    {
        var source = Expecting("many");
        Assert.Null(source.Query!.Expect);
        var result = GdrQueryBlockCheck.Check([source], "p.fwdata", false, TimeSpan.FromSeconds(5), null);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(GdrSyncCodes.QueryBlockInvalid, finding.Code);
        Assert.Contains("expect is a count", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockWithNoExpectedCountHasNoExpectedOnItsRun()
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-noexpect").FullName;
        try
        {
            var recorded = RecordedIndex(folder, false);
            var result = GdrQueryBlockCheck.Check([Source("q", Corpus)], Path.Combine(folder, "p.fwdata"), true, TimeSpan.FromSeconds(30), null,
                (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot));
            Assert.Null(Assert.Single(result.Runs).Expected);
            Assert.Empty(result.Findings);
        }
        finally { Directory.Delete(folder, true); }
    }

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

    private static GdrQueryBlockCheck.Seams Seam(Indexed recorded) => new(
        Corpus: (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot),
        CorpusBuilding: (line, baseDirectory, token) => GuideEvidenceRunner.Evaluate(line, baseDirectory, null, token, recorded.Builder, recorded.CacheRoot, allowIndexBuild: true));

    [Fact]
    public void BuildBuildsAColdIndexOnceForBlocksThatShareItAndThenChecksThem()
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-build").FullName;
        try
        {
            var recorded = RecordedIndex(folder, false, warm: false);
            var result = GdrQueryBlockCheck.Check([Source("a", Corpus), Source("b", Corpus)], Path.Combine(folder, "p.fwdata"), true,
                TimeSpan.FromSeconds(30), null, Seam(recorded), buildIndex: true);
            Assert.Equal(1, recorded.Builds);
            Assert.All(result.Runs, block => Assert.Equal(GdrQueryBlockStatus.Ok, block.Status));
            Assert.Equal([true, false], result.Runs.Select(block => block.IndexBuilt));
            Assert.Contains("built the parse index", result.Runs[0].Message, StringComparison.Ordinal);
            Assert.Equal(2L, result.Runs[0].Rows);
            Assert.Empty(result.Findings);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void WithoutBuildAColdBlockStaysNotBuiltBuildsNothingAndNamesTheGuideCheckBuildCommand()
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-nobuild").FullName;
        try
        {
            var recorded = RecordedIndex(folder, false, warm: false);
            var result = GdrQueryBlockCheck.Check([Source("q", Corpus)], Path.Combine(folder, "p.fwdata"), true, TimeSpan.FromSeconds(30), null, Seam(recorded));
            Assert.Equal(GdrQueryBlockStatus.NotBuilt, Assert.Single(result.Runs).Status);
            Assert.Equal(0, recorded.Builds);
            Assert.Contains("motif guide check --project '", Assert.Single(result.Findings).Message, StringComparison.Ordinal);
            Assert.Contains("--build", result.Findings[0].Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ABuildThatThrowsIsAFailedBlockAndTheNextBlockStillRuns()
    {
        var calls = 0;
        var cold = new GuideEvidenceResult(GuideEvidenceStatus.ColdIndex, null, "parse index not built: run x", "query.parse-index-cold", "x", 1, new Dictionary<string, double>());
        var seams = new GdrQueryBlockCheck.Seams(Corpus: (_, _, _) => cold, CorpusBuilding: (_, _, _) => ++calls == 1 ? throw new InvalidOperationException("no parser") : cold);
        var result = GdrQueryBlockCheck.Check([Source("a", Corpus), Source("b", Corpus)], "p.fwdata", true, TimeSpan.FromSeconds(30), null, seams, buildIndex: true);
        Assert.Equal(2, calls);
        Assert.Equal(GdrQueryBlockStatus.Failed, result.Runs[0].Status);
        Assert.Contains("no parser", result.Runs[0].Message, StringComparison.Ordinal);
        Assert.Equal(GdrQueryBlockStatus.NotBuilt, result.Runs[1].Status);
    }

    [Fact]
    public void AGroupByBlockReportsItsGroupsAsRowsAndItsWordsAndTokensAsEvidenceSize()
    {
        var folder = Directory.CreateTempSubdirectory("gdr-check-group").FullName;
        try
        {
            var recorded = RecordedIndex(folder, false, warm: false);
            var grouped = "motif query corpus forms --corpus words.tsv --grammar grammar.json forms where 'readings > 0' --group-by parse.state";
            var result = GdrQueryBlockCheck.Check([Source("g", grouped), Source("f", Corpus)], Path.Combine(folder, "p.fwdata"), true,
                TimeSpan.FromSeconds(30), null, Seam(recorded), buildIndex: true);
            var group = result.Runs[0];
            Assert.Equal(GdrQueryBlockStatus.Ok, group.Status);
            Assert.Equal(2L, group.Words);
            Assert.Equal(15L, group.Tokens);
            Assert.True(group.Rows is > 0 and <= 2, $"rows were {group.Rows}");
            Assert.Null(result.Runs[1].Words);
            Assert.Equal(15L, result.Runs[1].Tokens);
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
    public void AV1BlockThatRunsPastTheTimeoutIsFailedAndTheCheckStillReturns()
    {
        var release = new ManualResetEventSlim();
        var finished = false;
        var seams = new GdrQueryBlockCheck.Seams(Forms: _ =>
        {
            release.Wait(TimeSpan.FromSeconds(20));
            finished = true;
            return CommandOutcome<FormsQueryResponse>.Refused(new Refusal("query.no-trial-rows", FailureReason.NotFound, "none"));
        });
        var result = GdrQueryBlockCheck.Check([Source("slow", "motif query forms"), Source("g", "motif group suffix s")], "p.fwdata", true,
            TimeSpan.FromMilliseconds(200), null, seams);
        Assert.Equal([GdrQueryBlockStatus.Failed, GdrQueryBlockStatus.Refused], result.Runs.Select(block => block.Status));
        Assert.Contains("timed out", result.Runs[0].Message, StringComparison.Ordinal);
        Assert.False(finished);
        release.Set();
    }

    [Fact]
    public void TheTrialAdvisoryJsonCarriesNoQueryBlocksAndTheCheckJsonDoes()
    {
        var advisory = new GdrCheckResponse(true, true, [], [], new GdrSyncCounts(0, 0, 0, 0, 0, []));
        Assert.DoesNotContain("queryBlocks", System.Text.Json.JsonSerializer.Serialize(advisory), StringComparison.OrdinalIgnoreCase);
        var withBlocks = advisory with { QueryBlocks = [new GdrQueryBlockRun("q", "GDR-1", null, GdrQueryBlockStatus.Ok, null, "", 1, true, 1)] };
        Assert.Contains("queryBlocks", System.Text.Json.JsonSerializer.Serialize(withBlocks, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), StringComparison.Ordinal);
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
