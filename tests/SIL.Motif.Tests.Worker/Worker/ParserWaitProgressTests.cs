using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ParserWaitProgressTests : IDisposable
{
    private const string Project = "parser-wait-progress-project";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-parser-wait-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;

    public ParserWaitProgressTests()
    {
        Directory.CreateDirectory(_root);
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _jobs = new JobRepository(_database);
    }

    [Fact]
    public void ParserWaitRestoresStagePayloadAndKeepsItsStepCounts()
    {
        var claim = CreateClaim();
        var original = new JobStageProgress("import", 1, 4, "baseline");
        var originalJson = WriteProgress(claim, original);
        var wait = claim.ObserveParserWait(original);

        wait.Observe(true);
        var waiting = _jobs.Get(claim.JobId)!;
        var overlay = JsonSerializer.Deserialize<JobStageProgress>(waiting.ProgressJson!, MotifJson.CreateOptions());
        Assert.Equal(new JobStageProgress("waiting-for-parser-slot", 1, 4, "import: baseline"), overlay);

        wait.Observe(false);

        Assert.Equal(originalJson, _jobs.Get(claim.JobId)!.ProgressJson);
    }

    [Fact]
    public void ParserWaitRestoresExactTrialPayloadWithOptionalFields()
    {
        var claim = CreateClaim();
        var original = new TrialWordProgress(1, 3, "dog")
        {
            StoppedWords = [new StoppedParseWord("cat", "CAP", 54000)],
            SlowestWord = new ParseWordTiming("cat", 54000),
            PerWordLimitMs = 700,
        };
        var originalJson = WriteProgress(claim, original);
        var wait = claim.ObserveParserWait(new JobStageProgress("batch", 2, 2, "assessment"));

        wait.Observe(true);
        Assert.Equal("waiting-for-parser-slot",
            JsonSerializer.Deserialize<JobStageProgress>(_jobs.Get(claim.JobId)!.ProgressJson!,
                MotifJson.CreateOptions())!.Stage);
        wait.Observe(false);

        Assert.Equal(originalJson, _jobs.Get(claim.JobId)!.ProgressJson);
    }

    [Fact]
    public void ParserWaitRestoresSqlNullProgress()
    {
        var claim = CreateClaim();
        var wait = claim.ObserveParserWait(new JobStageProgress("facts", 2, 4, "candidate"));

        wait.Observe(true);
        Assert.NotNull(_jobs.Get(claim.JobId)!.ProgressJson);
        wait.Observe(false);

        Assert.Null(_jobs.Get(claim.JobId)!.ProgressJson);
    }

    [Fact]
    public void RepeatedParserWaitObservationsWriteAndRestoreOnlyOnce()
    {
        var claim = CreateClaim();
        var original = new TrialWordProgress(1, 3, "dog");
        var originalJson = WriteProgress(claim, original);
        var before = _jobs.Get(claim.JobId)!;
        var phase = new JobStageProgress("import", 1, 4, "baseline");
        var expectedWaitJson = JsonSerializer.Serialize(
            new JobStageProgress("waiting-for-parser-slot", 1, 4, "import: baseline"),
            MotifJson.CreateOptions());
        var wait = claim.ObserveParserWait(phase);

        wait.Observe(true);
        var waiting = _jobs.Get(claim.JobId)!;
        Assert.Equal(before.Version + 1, waiting.Version);
        Assert.Equal(expectedWaitJson, waiting.ProgressJson);

        wait.Observe(true);
        Assert.Equal(waiting, _jobs.Get(claim.JobId));

        wait.Observe(false);
        var restored = _jobs.Get(claim.JobId)!;
        Assert.Equal(before.Version + 2, restored.Version);
        Assert.Equal(originalJson, restored.ProgressJson);

        wait.Observe(false);
        wait.Observe(true);
        Assert.Equal(restored, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void ParserWaitRestoresAfterRealLeaseRenewalWithoutChangingItsSavedVersion()
    {
        var claim = CreateClaim(lease: TimeSpan.FromMinutes(5));
        var originalJson = WriteProgress(claim, new TrialWordProgress(0, 2, null));
        var wait = claim.ObserveParserWait(new JobStageProgress("batch", 2, 2, "assessment"));
        wait.Observe(true);
        var waiting = _jobs.Get(claim.JobId)!;

        Assert.True(new JobClaims(_database).Renew(claim.JobId, claim.Job.ClaimToken!,
            Stamp(DateTimeOffset.UtcNow.AddSeconds(1)), TimeSpan.FromMinutes(5)));
        var renewed = _jobs.Get(claim.JobId)!;
        Assert.Equal(waiting.Version, renewed.Version);
        Assert.Equal(waiting.ProgressJson, renewed.ProgressJson);

        wait.Observe(false);

        var restored = _jobs.Get(claim.JobId)!;
        Assert.Equal(originalJson, restored.ProgressJson);
        Assert.Equal(waiting.Version + 1, restored.Version);
    }

    [Fact]
    public void ParserWaitDoesNotRestoreOverANewerOrdinaryProgressWrite()
    {
        var claim = CreateClaim();
        var wait = claim.ObserveParserWait(new JobStageProgress("facts", 2, 4, "baseline"));
        wait.Observe(true);
        var newer = new JobStageProgress("measure", 3, 4, "candidate");

        claim.PublishStageProgress(newer);
        var afterNewer = _jobs.Get(claim.JobId)!;
        wait.Observe(false);

        Assert.Equal(afterNewer, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void ParserWaitDoesNotRestoreOverANewerOrdinaryTrialProgressWrite()
    {
        var claim = CreateClaim();
        var original = new TrialWordProgress(0, 3, null);
        var originalJson = WriteProgress(claim, original);
        var wait = claim.ObserveParserWait(new JobStageProgress("batch", 2, 2, "assessment"));
        wait.Observe(true);
        var newer = new TrialWordProgress(1, 3, "dog");

        claim.PublishTrialProgress(newer);
        var afterNewer = _jobs.Get(claim.JobId)!;
        wait.Observe(false);

        Assert.NotEqual(originalJson, afterNewer.ProgressJson);
        Assert.Equal(JsonSerializer.Serialize(newer, MotifJson.CreateOptions()), afterNewer.ProgressJson);
        Assert.Equal(afterNewer, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void SameHandleCannotRestoreAnOlderIdenticalWaitPayload()
    {
        var claim = CreateClaim();
        var phase = new JobStageProgress("import", 1, 4, "baseline");
        var first = claim.ObserveParserWait(phase);
        first.Observe(true);
        var firstWait = _jobs.Get(claim.JobId)!.ProgressJson;

        claim.PublishStageProgress(phase);
        var second = claim.ObserveParserWait(phase);
        second.Observe(true);
        var secondWait = _jobs.Get(claim.JobId)!;
        Assert.Equal(firstWait, secondWait.ProgressJson);

        first.Observe(false);
        Assert.Equal(secondWait, _jobs.Get(claim.JobId));
        second.Observe(false);

        Assert.Equal(JsonSerializer.Serialize(phase, MotifJson.CreateOptions()),
            _jobs.Get(claim.JobId)!.ProgressJson);
    }

    [Fact]
    public void IndependentHandlesCannotRestoreAcrossAnIdenticalWaitPayloadAba()
    {
        var (claim, record) = CreateClaimWithRecord();
        var other = new ClaimedJob(_jobs, record);
        var phase = new JobStageProgress("import", 1, 4, "baseline");
        var first = claim.ObserveParserWait(phase);
        first.Observe(true);
        var firstWait = _jobs.Get(claim.JobId)!.ProgressJson;

        other.PublishStageProgress(phase);
        var second = other.ObserveParserWait(phase);
        second.Observe(true);
        var secondWait = _jobs.Get(claim.JobId)!;
        Assert.Equal(firstWait, secondWait.ProgressJson);

        first.Observe(false);
        Assert.Equal(secondWait, _jobs.Get(claim.JobId));
        second.Observe(false);

        Assert.Equal(JsonSerializer.Serialize(phase, MotifJson.CreateOptions()),
            _jobs.Get(claim.JobId)!.ProgressJson);
    }

    [Fact]
    public void AnOldWaitCannotRestoreAfterItsRealClaimIsReclaimed()
    {
        var (claim, record, claims) = CreateExpiringClaim();
        var wait = claim.ObserveParserWait(new JobStageProgress("import", 1, 4, "baseline"));
        wait.Observe(true);
        var waiting = _jobs.Get(claim.JobId)!;
        var reclaimAt = DateTimeOffset.Parse(waiting.LeaseUntilUtc!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal).AddSeconds(1);
        var successorRecord = claims.Claim(Project, "successor", Stamp(reclaimAt), TimeSpan.FromMinutes(5));
        Assert.NotNull(successorRecord);
        Assert.NotEqual(record.ClaimToken, successorRecord!.ClaimToken);
        var successor = new ClaimedJob(_jobs, successorRecord);
        successor.PublishStageProgress(new JobStageProgress("facts", 2, 4, "candidate"));
        var beforeOldEnd = _jobs.Get(claim.JobId)!;

        wait.Observe(false);

        Assert.Equal(beforeOldEnd, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void TryUpdateClaimProgressSupportsMatchingNullTokenAndNullPayload()
    {
        var claim = CreateNullTokenRunningJob();
        var before = _jobs.Get(claim.JobId)!;
        var payload = "{\"progress\":\"value\"}";

        var written = _jobs.TryUpdateClaimProgress(claim.JobId, null, before.Version,
            null, payload);

        Assert.NotNull(written);
        Assert.Equal(payload, written!.ProgressJson);
        var restored = _jobs.TryUpdateClaimProgress(claim.JobId, null, written.Version,
            written.ProgressJson, nextProgressJson: null);
        Assert.NotNull(restored);
        Assert.Null(restored!.ProgressJson);
    }

    [Fact]
    public void TryUpdateClaimProgressRejectsNullCapturedTokenForRealClaim()
    {
        var claim = CreateClaim();
        var before = _jobs.Get(claim.JobId)!;

        Assert.False(string.IsNullOrWhiteSpace(before.ClaimToken));
        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, null, before.Version,
            before.ProgressJson, "{\"progress\":\"must-not-write\"}"));

        Assert.Equal(before, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void TryUpdateClaimProgressRejectsNonNullCapturedTokenForNullClaim()
    {
        var actualClaim = CreateClaim();
        var claim = CreateNullTokenRunningJob();
        var before = _jobs.Get(claim.JobId)!;
        var capturedToken = actualClaim.Job.ClaimToken;

        Assert.Null(before.ClaimToken);
        Assert.False(string.IsNullOrWhiteSpace(capturedToken));
        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, capturedToken, before.Version,
            before.ProgressJson, "{\"progress\":\"must-not-write\"}"));

        Assert.Equal(before, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void TryUpdateClaimProgressRejectsTokenVersionPayloadAndCancellationMismatches()
    {
        var claim = CreateClaim();
        var token = claim.Job.ClaimToken!;
        var initial = _jobs.Get(claim.JobId)!;
        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, "different-token", initial.Version,
            initial.ProgressJson, "{\"wrong\":true}"));
        Assert.Equal(initial, _jobs.Get(claim.JobId));

        var moved = _jobs.UpdateProgress(claim.JobId, "{\"progress\":\"moved\"}", initial.Version);
        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, token, initial.Version,
            moved.ProgressJson, "{\"wrong\":true}"));
        Assert.Equal(moved, _jobs.Get(claim.JobId));
        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, token, moved.Version,
            "{\"progress\":\"different\"}", "{\"wrong\":true}"));
        Assert.Equal(moved, _jobs.Get(claim.JobId));

        var cancelled = _jobs.RequestCancellation(claim.JobId);
        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, token, cancelled.Version,
            cancelled.ProgressJson, "{\"wrong\":true}"));
        Assert.Equal(cancelled, _jobs.Get(claim.JobId));
    }

    [Fact]
    public void StageProgressAfterStoredCancellationRemainsAdvisory()
    {
        var claim = CreateClaim();
        var cancelled = _jobs.RequestCancellation(claim.JobId);
        Assert.True(cancelled.CancellationRequested);
        Assert.Equal(cancelled, _jobs.Get(claim.JobId));

        claim.PublishStageProgress(new JobStageProgress("facts", 2, 4, "candidate"));

        Assert.Equal(cancelled, _jobs.Get(claim.JobId));
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.CompletedDryRunOnly)]
    [InlineData(JobStatus.CompletedWithAssessmentFailure)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Interrupted)]
    public void TryUpdateClaimProgressRejectsEveryTerminalStatus(JobStatus status)
    {
        var claim = CreateClaim();
        var current = _jobs.Get(claim.JobId)!;
        if (status is JobStatus.CompletedDryRunOnly or JobStatus.CompletedWithAssessmentFailure)
            current = _jobs.PublishDryRun(claim.JobId, "{}", current.Version);
        var category = status switch
        {
            JobStatus.Failed => JobFailureCategory.Unknown,
            JobStatus.Cancelled => JobFailureCategory.Cancellation,
            JobStatus.Interrupted => JobFailureCategory.Infrastructure,
            _ => JobFailureCategory.None,
        };
        var terminal = _jobs.Transition(claim.JobId, status, current.Version, category);

        Assert.Null(_jobs.TryUpdateClaimProgress(claim.JobId, claim.Job.ClaimToken, terminal.Version,
            terminal.ProgressJson, "{\"progress\":\"must-not-write\"}"));
        Assert.Equal(terminal, _jobs.Get(claim.JobId));
    }

    [Fact]
    public async Task OneBlockedQueueWorkResumesAfterTheWaitOverlayIsRestored()
    {
        var claim = CreateClaim();
        var phase = new JobStageProgress("grammar-health", 1, 2, "grammar-health");
        claim.PublishStageProgress(phase);
        var originalJson = _jobs.Get(claim.JobId)!.ProgressJson;
        var wait = claim.ObserveParserWait(phase);
        var slotNames = UniqueSlotNames("resume");
        using var firstSlot = new WorkerMutexOwner(slotNames[0], machineWide: true);
        using var secondSlot = new WorkerMutexOwner(slotNames[1], machineWide: true);
        Assert.True(firstSlot.TryAcquire());
        Assert.True(secondSlot.TryAcquire());

        using var queue = new MachinePanGlossQueue(slotNames);
        var failedAcquisition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.SlotWaitStarted = _ => failedAcquisition.TrySetResult();
        var workCount = 0;
        var pending = queue.RunAsync(claim.JobId, (_, _) =>
        {
            Interlocked.Increment(ref workCount);
            Assert.Equal(originalJson, _jobs.Get(claim.JobId)!.ProgressJson);
            claim.PublishTrialProgress(new TrialWordProgress(0, 2, "dog"));
            return Task.FromResult(23);
        }, CancellationToken.None, wait.Observe);

        try
        {
            await failedAcquisition.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var duringWait = _jobs.Get(claim.JobId)!;
            var waiting = JsonSerializer.Deserialize<JobStageProgress>(duringWait.ProgressJson!, MotifJson.CreateOptions());
            Assert.Equal(new JobStageProgress("waiting-for-parser-slot", 1, 2, "grammar-health"), waiting);
            Assert.False(pending.IsCompleted);

            firstSlot.Release();
            secondSlot.Release();
            Assert.Equal(23, await pending.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(1, Volatile.Read(ref workCount));
            Assert.Equal(JsonSerializer.Serialize(new TrialWordProgress(0, 2, "dog"), MotifJson.CreateOptions()),
                _jobs.Get(claim.JobId)!.ProgressJson);
        }
        finally
        {
            firstSlot.Release();
            secondSlot.Release();
            if (!pending.IsCompleted)
            {
                try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
            }
        }
    }

    [Fact]
    public async Task CancellingAParserWaitRestoresProgressWithoutStartingQueueWork()
    {
        var claim = CreateClaim();
        var phase = new JobStageProgress("import", 1, 4, "baseline");
        claim.PublishStageProgress(phase);
        var originalJson = _jobs.Get(claim.JobId)!.ProgressJson;
        var wait = claim.ObserveParserWait(phase);
        var slotNames = UniqueSlotNames("cancel");
        using var firstSlot = new WorkerMutexOwner(slotNames[0], machineWide: true);
        using var secondSlot = new WorkerMutexOwner(slotNames[1], machineWide: true);
        Assert.True(firstSlot.TryAcquire());
        Assert.True(secondSlot.TryAcquire());

        using var queue = new MachinePanGlossQueue(slotNames);
        using var cancellation = new CancellationTokenSource();
        var failedAcquisition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.SlotWaitStarted = _ => failedAcquisition.TrySetResult();
        var workCount = 0;
        var pending = queue.RunAsync(claim.JobId, (_, _) =>
        {
            Interlocked.Increment(ref workCount);
            return Task.FromResult(1);
        }, cancellation.Token, wait.Observe);

        try
        {
            await failedAcquisition.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("waiting-for-parser-slot", JsonSerializer.Deserialize<JobStageProgress>(
                _jobs.Get(claim.JobId)!.ProgressJson!, MotifJson.CreateOptions())!.Stage);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(originalJson, _jobs.Get(claim.JobId)!.ProgressJson);
            Assert.Equal(0, Volatile.Read(ref workCount));
        }
        finally
        {
            cancellation.Cancel();
            firstSlot.Release();
            secondSlot.Release();
            if (!pending.IsCompleted)
            {
                try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private ClaimedJob CreateClaim(TimeSpan? lease = null) => CreateClaimWithRecord(lease).Claim;

    private (ClaimedJob Claim, JobRecord Record) CreateClaimWithRecord(TimeSpan? lease = null)
    {
        var now = DateTimeOffset.UtcNow;
        var jobId = CanonicalId.Mint("job/").Value;
        _jobs.Create(jobId, Project, "trial", "{}", Stamp(now));
        var record = new JobClaims(_database).Claim(Project, "parser-wait-owner", Stamp(now.AddMilliseconds(1)),
            lease ?? TimeSpan.FromMinutes(5));
        Assert.NotNull(record);
        Assert.Equal(jobId, record!.JobId);
        Assert.False(string.IsNullOrWhiteSpace(record.ClaimToken));
        return (new ClaimedJob(_jobs, record), record);
    }

    private (ClaimedJob Claim, JobRecord Record, JobClaims Claims) CreateExpiringClaim()
    {
        var now = DateTimeOffset.UtcNow;
        var jobId = CanonicalId.Mint("job/").Value;
        _jobs.Create(jobId, Project, "trial", "{}", Stamp(now));
        var claims = new JobClaims(_database);
        var record = claims.Claim(Project, "original", Stamp(now.AddMilliseconds(1)), TimeSpan.FromSeconds(1));
        Assert.NotNull(record);
        return (new ClaimedJob(_jobs, record!), record!, claims);
    }

    private ClaimedJob CreateNullTokenRunningJob()
    {
        var jobId = CanonicalId.Mint("job/").Value;
        var queued = _jobs.Create(jobId, Project, "trial", "{}", Now());
        var running = _jobs.Transition(jobId, JobStatus.Running, queued.Version);
        Assert.Null(running.ClaimToken);
        return new ClaimedJob(_jobs, running);
    }

    private string WriteProgress(ClaimedJob claim, object value)
    {
        var json = JsonSerializer.Serialize(value, MotifJson.CreateOptions());
        var current = _jobs.Get(claim.JobId)!;
        _jobs.UpdateProgress(claim.JobId, json, current.Version);
        return json;
    }

    private static string Now() => JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);

    private static string Stamp(DateTimeOffset value) => JobTimestamp.FormatUtc(value);

    private static string[] UniqueSlotNames(string purpose)
    {
        var namespaceName = Environment.GetEnvironmentVariable(MachinePanGlossQueue.TestSlotNamespaceVariable)
            ?? "unscoped";
        var suffix = Guid.NewGuid().ToString("N");
        var prefix = $"Global\\MotifPanGlossSlot-{namespaceName}-parser-wait-{purpose}-{suffix}";
        return [$"{prefix}-0", $"{prefix}-1"];
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
