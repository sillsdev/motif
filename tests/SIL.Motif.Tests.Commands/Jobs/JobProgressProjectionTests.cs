using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class JobProgressProjectionTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ShowReturnsTrialWaitingStageWithoutInventingWordProgress()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        var expected = new JobStageProgress("waiting-for-parser-slot", 1, 1, "grammar-check");
        WriteProgress(fwDataPath, jobId, JsonSerializer.Serialize(expected, MotifJson.CreateOptions()));

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(expected, shown.Value!.StageProgress);
        Assert.Null(shown.Value.TrialProgress);
    }

    [Fact]
    public void ShowPreservesTrialWordProgressForAWordPayload()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        var expected = new TrialWordProgress(1, 2, "dog");
        WriteProgress(fwDataPath, jobId, JsonSerializer.Serialize(expected, MotifJson.CreateOptions()));

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(JsonSerializer.Serialize(expected, MotifJson.CreateOptions()),
            JsonSerializer.Serialize(shown.Value!.TrialProgress, MotifJson.CreateOptions()));
        Assert.Null(shown.Value.StageProgress);
    }

    [Fact]
    public void ShowPreservesOptionalTrialWordProgressFields()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        var expected = new TrialWordProgress(1, 2, "dog")
        {
            StoppedWords = [new StoppedParseWord("cat", "CAP", 54000)],
            SlowestWord = new ParseWordTiming("cat", 54000),
            PerWordLimitMs = 700,
        };
        var progressJson = JsonSerializer.Serialize(expected, MotifJson.CreateOptions());
        WriteProgress(fwDataPath, jobId, progressJson);

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(progressJson,
            JsonSerializer.Serialize(shown.Value!.TrialProgress, MotifJson.CreateOptions()));
    }

    [Theory]
    [InlineData("{\"Completed\":\"one\",\"Total\":1,\"CurrentWord\":null}")]
    [InlineData("{\"Stage\":\"import\",\"Step\":1,\"Steps\":1,\"Completed\":0,\"Total\":1,\"CurrentWord\":null}")]
    [InlineData("{\"Stage\":\"import\",\"Stage\":\"facts\",\"Step\":1,\"Steps\":2}")]
    [InlineData("{\"Stage\":\"import\",\"Steps\":1}")]
    [InlineData("{\"Stage\":\"import\",\"Step\":\"one\",\"Steps\":1}")]
    [InlineData("{\"Completed\":0,\"Total\":1,\"StoppedWords\":[{\"Word\":\"cat\",\"Word\":\"dog\",\"Reason\":\"CAP\",\"ElapsedMs\":1}]}")]
    [InlineData("{\"Completed\":0,\"Total\":1,\"SlowestWord\":\"cat\"}")]
    [InlineData("{\"Completed\":0,\"Total\":1,\"PerWordLimitMs\":\"fast\"}")]
    [InlineData("{\"Completed\":0,\"Total\":1,\"SlowestWord\":{\"Word\":\"cat\",\"ElapsedMs\":\"fast\"}}")]
    public void ShowRefusesMalformedMixedAndDuplicateProgressShapes(string progressJson)
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        WriteProgress(fwDataPath, jobId, progressJson);

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.False(shown.Succeeded);
        Assert.Equal("store.inconsistent", shown.Refusal?.Code);
    }

    [Fact]
    public void ShowSuppressesAWaitingStageOnATerminalRow()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        WriteProgress(fwDataPath, jobId,
            JsonSerializer.Serialize(new JobStageProgress("waiting-for-parser-slot", 1, 2, "facts"),
                MotifJson.CreateOptions()));
        TransitionToFailure(fwDataPath, jobId);

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(JobStatus.Failed, shown.Value!.Status);
        Assert.Null(shown.Value.StageProgress);
        Assert.Null(shown.Value.TrialProgress);
    }

    [Fact]
    public void ShowPreservesFinalWordProgressOnATerminalRow()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        var finalProgress = new TrialWordProgress(1, 1, null)
        {
            StoppedWords = [new StoppedParseWord("cat", "CAP", 54000)],
        };
        WriteProgress(fwDataPath, jobId,
            JsonSerializer.Serialize(finalProgress, MotifJson.CreateOptions()));
        TransitionToFailure(fwDataPath, jobId);

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(JobStatus.Failed, shown.Value!.Status);
        Assert.Equal(JsonSerializer.Serialize(finalProgress, MotifJson.CreateOptions()),
            JsonSerializer.Serialize(shown.Value.TrialProgress, MotifJson.CreateOptions()));
        Assert.Null(shown.Value.StageProgress);
    }

    [Fact]
    public async Task WaitTimeoutReportsTheLastObservedStageAndDetail()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        WriteProgress(fwDataPath, jobId,
            JsonSerializer.Serialize(new JobStageProgress("waiting-for-parser-slot", 1, 2, "facts: baseline"),
                MotifJson.CreateOptions()));

        var waited = await JobWait.WaitAsync(fwDataPath, jobId, null, CancellationToken.None,
            TimeSpan.Zero, MotifProductVersion.CurrentText);

        Assert.False(waited.Succeeded);
        Assert.Equal("job.wait-timeout", waited.Refusal?.Code);
        Assert.Equal("waiting-for-parser-slot", waited.Refusal!.Facts!["stage"]);
        Assert.Equal("facts: baseline", waited.Refusal.Facts["stageDetail"]);
        Assert.Contains("waiting-for-parser-slot", waited.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("facts: baseline", waited.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowLeavesBothProgressViewsNullWhenTheJobHasNoProgress()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);

        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Null(shown.Value!.TrialProgress);
        Assert.Null(shown.Value.StageProgress);
    }

    [Fact]
    public async Task AWaitOnAQueuedJobWakesTheRunnerAndTheTimeoutSaysNoRunnerClaimedIt()
    {
        var jobId = CreateRunningTrial(out var fwDataPath, leaveQueued: true);
        var wakes = 0;

        var waited = await JobWait.WaitAsync(fwDataPath, jobId, null, CancellationToken.None,
            TimeSpan.FromMilliseconds(500), MotifProductVersion.CurrentText,
            wakeRunner: () => wakes++, rewakeAfter: TimeSpan.Zero);

        Assert.Equal("job.wait-timeout", waited.Refusal?.Code);
        Assert.True(wakes >= 1, "A queued job's wait never woke a runner.");
        Assert.Equal("true", waited.Refusal!.Facts!["unclaimed"]);
        Assert.Contains("No runner has claimed it", waited.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWaitOnAClaimedJobDoesNotWakeAnotherRunner()
    {
        var jobId = CreateRunningTrial(out var fwDataPath);
        var wakes = 0;

        var waited = await JobWait.WaitAsync(fwDataPath, jobId, null, CancellationToken.None,
            TimeSpan.FromMilliseconds(500), MotifProductVersion.CurrentText,
            wakeRunner: () => wakes++, rewakeAfter: TimeSpan.Zero);

        Assert.Equal("job.wait-timeout", waited.Refusal?.Code);
        Assert.Equal(0, wakes);
        Assert.False(waited.Refusal!.Facts!.ContainsKey("unclaimed"));
    }

    [Fact]
    public async Task WakesAreRateLimitedToOnePerInterval()
    {
        var jobId = CreateRunningTrial(out var fwDataPath, leaveQueued: true);
        var wakes = 0;

        await JobWait.WaitAsync(fwDataPath, jobId, null, CancellationToken.None, TimeSpan.FromMilliseconds(2500),
            MotifProductVersion.CurrentText, wakeRunner: () => wakes++, rewakeAfter: TimeSpan.FromSeconds(1));

        Assert.InRange(wakes, 1, 3);
    }

    [Fact]
    public async Task CancellingDuringAWakeCancelsTheWaitAndTheJob()
    {
        var jobId = CreateRunningTrial(out var fwDataPath, leaveQueued: true);
        using var source = new CancellationTokenSource();

        var waited = await JobWait.WaitAsync(fwDataPath, jobId, null, source.Token, TimeSpan.FromSeconds(30),
            MotifProductVersion.CurrentText, wakeRunner: source.Cancel, rewakeAfter: TimeSpan.Zero);

        Assert.Equal("job.wait-cancelled", waited.Refusal?.Code);
        var shown = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, MotifProductVersion.CurrentText));
        Assert.Equal(JobStatus.Cancelled, shown.Value!.Status);
    }

    [Fact]
    public async Task AJobAClaimedDuringTheWaitIsNotReportedAsUnclaimed()
    {
        var jobId = CreateRunningTrial(out var fwDataPath, leaveQueued: true);

        var waited = await JobWait.WaitAsync(fwDataPath, jobId, null, CancellationToken.None,
            TimeSpan.FromMilliseconds(600), MotifProductVersion.CurrentText,
            wakeRunner: () => Claim(fwDataPath, jobId), rewakeAfter: TimeSpan.Zero);

        Assert.Equal("job.wait-timeout", waited.Refusal?.Code);
        Assert.False(waited.Refusal!.Facts!.ContainsKey("unclaimed"));
        Assert.DoesNotContain("No runner has claimed it", waited.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledQueuedJobIsStillReportedAsUnclaimed()
    {
        var jobId = CreateRunningTrial(out var fwDataPath, leaveQueued: true);

        var waited = await JobWait.WaitAsync(fwDataPath, jobId, null, CancellationToken.None,
            TimeSpan.Zero, MotifProductVersion.CurrentText, cancelOnTimeout: true);

        Assert.Equal("true", waited.Refusal!.Facts!["unclaimed"]);
    }

    [Fact]
    public void RunnerWarningsForwardEachDistinctMessageOnce()
    {
        var seen = new List<string>();
        var once = RunnerWarnings.Once(seen.Add)!;

        once("a");
        once("a");
        once("b");

        Assert.Equal(["a", "b"], seen);
    }

    private static void Claim(string fwDataPath, string jobId)
    {
        var project = ProjectStoreCommand.Locate(fwDataPath);
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema,
            MotifProductVersion.CompatibilityVersion(MotifProductVersion.CurrentText));
        using var database = catalog.OpenOwned(project);
        var jobs = new JobRepository(database);
        var queued = jobs.Get(jobId)!;
        if (queued.Status == JobStatus.Queued) jobs.Transition(queued, JobStatus.Running);
    }

    private string CreateRunningTrial(out string fwDataPath, bool leaveQueued = false)
    {
        fwDataPath = pristine.CopyProjectFile();
        var project = ProjectStoreCommand.Locate(fwDataPath);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposal = Draft(proposalId);
        var proposalJson = ProposalCommands.BuildProposalJson(proposal);
        var digest = IntentDigest.Compute(ProposalJsonParser.Parse(proposalJson));
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema,
            MotifProductVersion.CompatibilityVersion(MotifProductVersion.CurrentText));

        using (var database = catalog.OpenOwned(project))
        {
            new ProposalRepository(database).SaveRevision(new ProposalRevisionRecord(proposalId, digest,
                proposalJson, "proposed", null, null, null));
        }

        var enqueued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(fwDataPath,
            MotifProductVersion.CurrentText, proposalId.Value, Words: ["cat"]));
        Assert.True(enqueued.Succeeded, enqueued.Refusal?.Message);
        var jobId = enqueued.Value!.JobId;

        if (leaveQueued) return jobId;
        using (var database = catalog.OpenOwned(project))
        {
            var jobs = new JobRepository(database);
            var queued = jobs.Get(jobId)!;
            jobs.Transition(queued, JobStatus.Running);
        }

        return jobId;
    }

    private static void WriteProgress(string fwDataPath, string jobId, string progressJson)
    {
        var project = ProjectStoreCommand.Locate(fwDataPath);
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema,
            MotifProductVersion.CompatibilityVersion(MotifProductVersion.CurrentText));
        using var database = catalog.OpenOwned(project);
        var jobs = new JobRepository(database);
        var running = jobs.Get(jobId)!;
        jobs.UpdateProgress(jobId, progressJson, running.Version);
    }

    private static void TransitionToFailure(string fwDataPath, string jobId)
    {
        var project = ProjectStoreCommand.Locate(fwDataPath);
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema,
            MotifProductVersion.CompatibilityVersion(MotifProductVersion.CurrentText));
        using var database = catalog.OpenOwned(project);
        var jobs = new JobRepository(database);
        var running = jobs.Get(jobId)!;
        jobs.Transition(jobId, JobStatus.Failed, running.Version, JobFailureCategory.Unknown, "{}");
    }

    private static DraftDocument Draft(CanonicalId proposalId)
    {
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(new { ws = "en", text = "progress fixture" }));
        return new DraftDocument
        {
            ProposalId = proposalId.Value,
            ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
            Operations =
            [
                new DraftOperation
                {
                    OperationId = CanonicalId.Mint("operation/").Value,
                    Kind = "lexical/lexSense/setGloss",
                    Target = CanonicalId.Mint("lexSense/").Value,
                    After = after.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone(),
                            StringComparer.Ordinal),
                },
            ],
        };
    }
}
