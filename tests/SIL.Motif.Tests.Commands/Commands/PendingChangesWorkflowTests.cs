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
        using var environment = new RunnerEnvironment(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);

        var initial = LoadPending(path);
        var added = PutChange(path, initial.Revision, wordformId, "review-word");
        Assert.Equal("Texts", Assert.Single(added.Changes).OriginPage);
        Assert.True(ProposalCommands.Label(new LabelRequest(path, ProductVersion, PendingChanges.DraftName,
            "Correct word spelling")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(path, ProductVersion, PendingChanges.DraftName,
            "Correct the spelling status of the selected word.")).Succeeded);
        var pending = LoadPending(path);

        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, pending.DraftId!,
            pending.Revision, ["review-word"]), new Progress<MeasureProgress>(), CancellationToken.None);

        Assert.True(measured.Succeeded, measured.Refusal?.Message + " " + LastJob(path));
        Assert.True(measured.Value!.EvidenceComplete);
        Assert.Equal(JobStatus.Completed, JobCommands.Show(new ShowJobRequest(path,
            measured.Value.JobId, ProductVersion)).Value!.Status);
        Assert.Equal(pending.Revision, LoadPending(path).Revision);
        var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, pending.DraftId!,
            pending.Revision, "test-user"));

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.Equal(pending.DraftId, applied.Value!.ProposalId);
        Assert.Empty(LoadPending(path).Changes);
        using var database = ProjectMotifDatabase.Open(path);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(pending.DraftId!));
        Assert.Equal("Correct word spelling", proposal.Label);
        Assert.Equal("Correct the spelling status of the selected word.", proposal.Comment);
        Assert.Equal(1L, CountReceipts(database, pending.DraftId!));

        var empty = LoadPending(path);
        var second = PutChange(path, empty.Revision, secondWordformId, "review-second");
        var stale = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, second.DraftId!,
            "stale-revision", "test-user"));
        Assert.Equal("review.changes-changed", stale.Refusal?.Code);
        var withoutTrial = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, second.DraftId!,
            second.Revision, "test-user"));
        Assert.Equal("apply.not-ready", withoutTrial.Refusal?.Code);
        var reopened = LoadPending(path);
        Assert.Equal(second.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);

        var currentAssessmentId = new AssessmentRepository(database).GetCurrent()!.AssessmentId;
        var secondTrial = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, reopened.DraftId!,
            reopened.Revision, ["review-second"], currentAssessmentId),
            new Progress<MeasureProgress>(), CancellationToken.None);
        Assert.True(secondTrial.Succeeded, secondTrial.Refusal?.Message + " " + LastJob(path));
        Assert.True(secondTrial.Value!.EvidenceComplete);
        Assert.Equal(JobStatus.Completed, JobCommands.Show(new ShowJobRequest(path,
            secondTrial.Value.JobId, ProductVersion)).Value!.Status);
        var secondApply = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, reopened.DraftId!,
            reopened.Revision, "test-user"));
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
        using var environment = new RunnerEnvironment(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "unmeasured-word");

        var outcome = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, pending.DraftId!,
            pending.Revision, "test-user"));

        Assert.Equal("apply.not-ready", outcome.Refusal?.Code);
        var reopened = LoadPending(path);
        Assert.Equal(pending.DraftId, reopened.DraftId);
        Assert.Single(reopened.Changes);
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
        using var environment = new RunnerEnvironment(root, suppressKick: true);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PutChange(path, LoadPending(path).Revision, wordformId, "cancelled-word");
        using var cancellation = new CancellationTokenSource();
        var applying = Task.Run(() => PendingChangesWorkflow.Apply(new ApplyPendingRequest(path,
            pending.DraftId!, pending.Revision, "test-user"), cancellation.Token));
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

    private static async Task<string> WaitForLatestJobAsync(string path, string kind)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
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

    private static string LastJob(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status || ': ' || COALESCE(ResultJson, '') FROM Jobs ORDER BY rowid DESC LIMIT 1;";
        return command.ExecuteScalar()?.ToString() ?? "no job";
    }

    private sealed class RunnerEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

        public RunnerEnvironment(string root, bool suppressKick = false)
        {
            Set(RunnerOptions.RootVariable, root);
            Set("MOTIF_WORKER_EXE", BuildOutput.Worker);
            Set("MOTIF_PANGLOSS_EXE", FakeParser.ExecutablePath);
            Set(RunnerOptions.NamespaceVariable, Guid.NewGuid().ToString("N"));
            Set(RunnerOptions.IdleVariable, "1");
            Set(RunnerKick.SuppressVariable, suppressKick ? "1" : null);
        }

        private void Set(string name, string? value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
