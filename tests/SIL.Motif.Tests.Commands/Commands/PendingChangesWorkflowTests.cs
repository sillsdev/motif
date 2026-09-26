using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingChangesWorkflowTests(PristineProjectFixture pristine)
{
    [Fact]
    public void PendingChangeWorkflowsAreCataloguedForBothFrontEnds()
    {
        Assert.Contains(CommandCatalog.All, command => command.Name == "apply --all-pending");
        Assert.Contains(CommandCatalog.All, command => command.Name == "trial --pending");
    }

    [Fact]
    public async Task ASeededTrialFlowsThroughReviewApplyWithoutRewritingTheDraft()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        Guid secondWordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
        {
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("review-word", scratch.DefaultVernWs)).Guid;
            secondWordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("review-second", scratch.DefaultVernWs)).Guid;
        });
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.Process(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);

        var initial = LoadPending(path);
        var added = PutChange(path, initial.Revision, wordformId, "review-word");
        Assert.Equal("Texts", Assert.Single(added.Changes).OriginPage);
        Assert.True(ProposalCommands.Label(new LabelRequest(path, ProductVersion, PendingChanges.DraftName,
            "Correct word spelling")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(path, ProductVersion, PendingChanges.DraftName,
            "Correct the spelling status of the selected word.")).Succeeded);
        var pending = LoadPending(path);

        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, null, null,
            ["review-word"]), new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);

        Assert.True(measured.Succeeded, measured.Refusal?.Message + " " + LastJob(path));
        Assert.True(measured.Value!.EvidenceComplete);
        Assert.Equal(pending.Revision, measured.Value.Revision);
        Assert.Equal(JobStatus.Completed, JobCommands.Show(new ShowJobRequest(path,
            measured.Value.JobId, ProductVersion)).Value!.Status);
        using (var alreadyCancelled = new CancellationTokenSource())
        {
            alreadyCancelled.Cancel();
            var completed = await JobWait.WaitAsync(path, measured.Value.JobId, null,
                alreadyCancelled.Token, JobCommands.DefaultWaitTimeout, ProductVersion);
            Assert.True(completed.Succeeded);
            Assert.Equal(JobStatus.Completed, completed.Value!.Status);
        }
        Assert.Equal(pending.Revision, LoadPending(path).Revision);
        var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, pending.DraftId!,
            pending.Revision, "test-user"), runnerLauncher: runner);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied);
        Assert.Equal(pending.DraftId, applied.Value.Receipt!.ProposalId);
        Assert.Empty(LoadPending(path).Changes);
        using var database = ProjectMotifDatabase.Open(path);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(pending.DraftId!));
        Assert.Equal("Correct word spelling", proposal.Label);
        Assert.Equal("Correct the spelling status of the selected word.", proposal.Comment);
        Assert.Equal(1L, CountReceipts(database, pending.DraftId!));

        var empty = LoadPending(path);
        var second = PutChange(path, empty.Revision, secondWordformId, "review-second");
        var stale = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, second.DraftId!,
            "stale-revision", "test-user"), runnerLauncher: runner);
        Assert.Equal("apply.changes-changed", stale.Refusal?.Code);
        var withoutTrial = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, second.DraftId!,
            second.Revision, "test-user"), runnerLauncher: runner);
        Assert.Equal("apply.not-ready", withoutTrial.Refusal?.Code);
        var reopened = LoadPending(path);
        Assert.Equal(second.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);

        var currentAssessmentId = new AssessmentRepository(database).GetCurrent()!.AssessmentId;
        var secondTrial = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, reopened.DraftId!,
            reopened.Revision, ["review-second"], currentAssessmentId),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(secondTrial.Succeeded, secondTrial.Refusal?.Message + " " + LastJob(path));
        Assert.True(secondTrial.Value!.EvidenceComplete);
        Assert.Equal(JobStatus.Completed, JobCommands.Show(new ShowJobRequest(path,
            secondTrial.Value.JobId, ProductVersion)).Value!.Status);
        var secondApply = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, reopened.DraftId!,
            reopened.Revision, "test-user"), runnerLauncher: runner);
        Assert.True(secondApply.Succeeded, secondApply.Refusal?.Message);
        var secondProposal = new ProposalRepository(database).Get(CanonicalId.Parse(reopened.DraftId!));
        Assert.Equal("Changes to word analyses", secondProposal.Label);
        Assert.Equal("Changes to word analyses and spelling.", secondProposal.Comment);
    }

    [Fact]
    public void ARefusalAfterFinalizeReopensTheDraft()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("unmeasured-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.Process(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "unmeasured-word");

        var outcome = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, pending.DraftId!,
            pending.Revision, "test-user"), runnerLauncher: runner);

        Assert.Equal("apply.not-ready", outcome.Refusal?.Code);
        var reopened = LoadPending(path);
        Assert.Equal(pending.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);
    }

    [Fact]
    public void ApplyAllPendingWhileTheProjectIsHeldIsBusyAndReopensTheChanges()
    {
        string path;
        Guid wordformId = Guid.Empty;
        using (var scratch = pristine.NewScratch())
        {
            NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
                wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("held-apply", scratch.DefaultVernWs)).Guid);
            new FwDataProjectLoader().Save(scratch);
            path = scratch.ProjectId.Path;
        }
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "held-apply");

        CommandOutcome<ApplyPendingResult> outcome;
        using (new FileStream(path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            outcome = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null, "test-user"),
                runnerLauncher: runner);

        Assert.Equal(FailureReason.Busy, outcome.Refusal?.Reason);
        Assert.Equal(3, FailureEnvelope.ExitCodeFor(outcome.Refusal!.Reason));
        var reopened = LoadPending(path);
        Assert.Equal(pending.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);
    }

    [Fact]
    public void ApplyNeverReportsNothingPendingWhileThePendingDraftHoldsAnOperation()
    {
        string path;
        using (var scratch = pristine.NewScratch())
            path = scratch.ProjectId.Path;
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, ProductVersion, PendingChanges.DraftName,
            "a label")).Succeeded);
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, ProductVersion,
            PendingChanges.DraftName, CanonicalId.Mint().Value, "en", "a gloss without a change id"));
        Assert.True(added.Succeeded, added.Refusal?.Message);

        var outcome = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null, "test-user"),
            runnerLauncher: runner);

        Assert.False(outcome.Succeeded && !outcome.Value!.Applied,
            "The save boundary reported nothing to apply while the Draft held an operation.");
    }

    [Fact]
    public void ApplyingWhenNoChangesArePendingReturnsTheNoWorkOutcome()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var root = NewManagedRoot(path);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);

        var outcome = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null, "test-user"));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.False(outcome.Value!.Applied);
        Assert.Null(outcome.Value.Receipt);
        using var database = ProjectMotifDatabase.Open(path);
        Assert.Equal(0L, CountAllReceipts(database));
    }

    [Fact]
    public async Task MeasureRefusesAStaleRevisionBeforeQueueingATrial()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("stale-measure", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "stale-measure");
        using var cancellation = new CancellationTokenSource();

        var measuring = PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, pending.DraftId!,
            "sha256:stale", ["stale-measure"]), new Progress<MeasureProgress>(), cancellation.Token,
                runnerLauncher: runner);
        if (await Task.WhenAny(measuring, Task.Delay(TimeSpan.FromSeconds(2))) != measuring)
            cancellation.Cancel();
        var outcome = await measuring.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("trial.changes-changed", outcome.Refusal?.Code);
        Assert.Equal(0L, CountJobs(path, JobCommands.TrialKind));
    }

    [Fact]
    public async Task MeasureWithNoIdentitiesQueuesTheCurrentDraftWithoutOpeningTheProject()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("identity-measure", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "identity-measure");
        CommandOutcome<MeasurePendingResult> outcome;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, null, null,
                ["identity-measure"]), new Progress<MeasureProgress>(), CancellationToken.None,
                TimeSpan.FromMilliseconds(1), runnerLauncher: runner).WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Equal("job.wait-timeout", outcome.Refusal?.Code);
        using var database = ProjectMotifDatabase.Open(path);
        var job = new JobRepository(database).Get(outcome.Refusal!.Facts!["jobId"]);
        var input = TrialJobInput.Parse(Assert.IsType<JobRecord>(job).InputJson);
        using var proposal = System.Text.Json.JsonDocument.Parse(input.ProposalJson);
        Assert.Equal(pending.DraftId, proposal.RootElement.GetProperty("proposalId").GetString());
    }

    [Fact]
    public async Task MeasureWithNoPendingDraftIsARefusalWithoutEnqueueingATrial()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);

        var outcome = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, null, null, []),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);

        Assert.Equal("trial.nothing-pending", outcome.Refusal?.Code);
        Assert.Equal(0L, CountJobs(path, JobCommands.TrialKind));
    }

    [Fact]
    public async Task ATimedOutDryRunIsCancelledBeforeItsDraftIsReopened()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("timed-out-dry-run", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "timed-out-dry-run");

        var applying = Task.Run(() => PendingChangesWorkflow.Apply(new ApplyPendingRequest(path,
            pending.DraftId!, pending.Revision, "test-user"), dryRunTimeout: TimeSpan.FromMilliseconds(1),
                runnerLauncher: runner));
        var jobId = await WaitForLatestJobAsync(path, JobCommands.DryRunKind);
        var outcome = await applying.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("job.wait-timeout", outcome.Refusal?.Code);
        Assert.Equal("true", outcome.Refusal!.Facts!["jobCancelled"]);
        Assert.DoesNotContain("jobs show", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Cancelled, JobCommands.Show(new ShowJobRequest(path, jobId, ProductVersion))
            .Value!.Status);
        var reopened = LoadPending(path);
        Assert.Equal(pending.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);
    }

    [Fact]
    public async Task CancellingAMeasurementCancelsTheTrialJob()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("cancelled-trial", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "cancelled-trial");
        using var cancellation = new CancellationTokenSource();
        var measuring = PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, pending.DraftId!,
            pending.Revision, ["cancelled-trial"]), new Progress<MeasureProgress>(), cancellation.Token,
                runnerLauncher: runner);
        var jobId = await WaitForLatestJobAsync(path, JobCommands.TrialKind);
        await Task.Delay(450);
        cancellation.Cancel();
        var outcome = await measuring.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("job.wait-cancelled", outcome.Refusal?.Code);
        Assert.Equal(JobStatus.Cancelled, JobCommands.Show(new ShowJobRequest(path, jobId, ProductVersion))
            .Value!.Status);
    }

    [Fact]
    public async Task AnUnboundedWaitKeepsPollingUntilTheCallerCancels()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var queued = JobCommands.EnqueueBaselineRefresh(new EnqueueBaselineRefreshRequest(path, ProductVersion));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var cancellation = new CancellationTokenSource();

        var waiting = JobWait.WaitAsync(path, queued.Value!.JobId, null, cancellation.Token, null, ProductVersion);
        await Task.Delay(450);
        cancellation.Cancel();
        var outcome = await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("job.wait-cancelled", outcome.Refusal?.Code);
        Assert.Equal("true", outcome.Refusal?.Facts?["jobCancelled"]);
        Assert.Equal(JobStatus.Cancelled, JobCommands.Show(new ShowJobRequest(path, queued.Value.JobId,
            ProductVersion)).Value!.Status);
    }

    [Fact]
    public async Task AJobThatFinishesAsTheWaitIsCancelledReturnsItsTerminalResult()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var jobId = QueueJobThatFinishesOnFirstPoll(path, out var finishOnFirstPoll);
        using var cancellation = new CancellationTokenSource();
        var progress = new ActionProgress<JobStatusResponse>(_ =>
        {
            if (finishOnFirstPoll()) cancellation.Cancel();
        });

        var outcome = await JobWait.WaitAsync(path, jobId, progress, cancellation.Token, null, ProductVersion)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(JobStatus.Completed, outcome.Value!.Status);
    }

    [Fact]
    public async Task AJobThatFinishesAsTheWaitTimesOutReturnsItsTerminalResult()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var jobId = QueueJobThatFinishesOnFirstPoll(path, out var finishOnFirstPoll);
        var progress = new ActionProgress<JobStatusResponse>(_ => finishOnFirstPoll());

        var outcome = await JobWait.WaitAsync(path, jobId, progress, CancellationToken.None, TimeSpan.Zero,
            ProductVersion, cancelOnTimeout: true).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(JobStatus.Completed, outcome.Value!.Status);
    }

    [Fact]
    public async Task ATimeoutThatOnlyRequestsCancellationOfARunningJobSaysSo()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var queued = JobCommands.EnqueueBaselineRefresh(new EnqueueBaselineRefreshRequest(path, ProductVersion));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using (var database = ProjectMotifDatabase.Open(path))
            new JobRepository(database).Transition(queued.Value!.JobId, JobStatus.Running);

        var outcome = await JobWait.WaitAsync(path, queued.Value!.JobId, null, CancellationToken.None,
            TimeSpan.Zero, ProductVersion, cancelOnTimeout: true).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("job.wait-timeout", outcome.Refusal?.Code);
        Assert.Equal("true", outcome.Refusal!.Facts!["jobCancelled"]);
        Assert.Contains("requested cancellation", outcome.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingDuringTheDryRunWaitCancelsTheJobAndLeavesTheDraftOpen()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("cancelled-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = NewManagedRoot(path);
        var runner = IsolatedRunner.None(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "cancelled-word");
        using var cancellation = new CancellationTokenSource();
        var applying = Task.Run(() => PendingChangesWorkflow.Apply(new ApplyPendingRequest(path,
            pending.DraftId!, pending.Revision, "test-user"), cancellation.Token, runnerLauncher: runner));
        var jobId = await WaitForLatestJobAsync(path, JobCommands.DryRunKind);
        cancellation.Cancel();
        var outcome = await applying.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(outcome.Succeeded);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal!.Reason);
        Assert.Equal("job.wait-cancelled", outcome.Refusal.Code);
        Assert.Equal(JobStatus.Cancelled, JobCommands.Show(new ShowJobRequest(path, jobId, ProductVersion))
            .Value!.Status);
        var reopened = LoadPending(path);
        Assert.Equal(pending.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);
    }

    private static string ProductVersion => MotifProductVersion.CurrentText;

    private static string NewManagedRoot(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "pending-workflow-root");
        Directory.CreateDirectory(root);
        return root;
    }

    private static PendingChangesSnapshot LoadPending(string path) => PendingChanges.Load(
        new PendingChangesRequest(path, ProductVersion)).Value!;

    private static PendingChangesSnapshot PutChange(string path, string revision, Guid wordformId, string word) =>
        PendingChanges.Put(new PutPendingChangeRequest(path, ProductVersion, revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, word, OriginPage: "Texts"))).Value!;

    private static long CountReceipts(SIL.Motif.Host.Store.MotifDatabase database, string proposalId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        return (long)command.ExecuteScalar()!;
    }

    private static long CountAllReceipts(SIL.Motif.Host.Store.MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts;";
        return (long)command.ExecuteScalar()!;
    }

    private static long CountJobs(string path, string kind)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Jobs WHERE Kind = $kind;";
        command.Parameters.AddWithValue("$kind", kind);
        return (long)command.ExecuteScalar()!;
    }

    private static async Task<string> WaitForLatestJobAsync(string path, string kind, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var database = ProjectMotifDatabase.Open(path);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT JobId FROM Jobs WHERE Kind = $kind ORDER BY rowid DESC LIMIT 1;";
            command.Parameters.AddWithValue("$kind", kind);
            if (command.ExecuteScalar()?.ToString() is { Length: > 0 } jobId) return jobId;
            await Task.Delay(20);
        }
        throw new TimeoutException("The workflow did not enqueue its job.");
    }

    // Completes the job inside the first progress report, after the poll read it as queued.
    private static string QueueJobThatFinishesOnFirstPoll(string path, out Func<bool> finishOnFirstPoll)
    {
        var queued = JobCommands.EnqueueBaselineRefresh(new EnqueueBaselineRefreshRequest(path, ProductVersion));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        var jobId = queued.Value!.JobId;
        var finished = false;
        finishOnFirstPoll = () =>
        {
            if (finished) return false;
            finished = true;
            using var database = ProjectMotifDatabase.Open(path);
            var jobs = new JobRepository(database);
            jobs.Transition(jobId, JobStatus.Running);
            jobs.Transition(jobId, JobStatus.Completed, "{}");
            return true;
        };
        return jobId;
    }

    private sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static string LastJob(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status || ': ' || COALESCE(ResultJson, '') FROM Jobs ORDER BY rowid DESC LIMIT 1;";
        return command.ExecuteScalar()?.ToString() ?? "no job";
    }
}
