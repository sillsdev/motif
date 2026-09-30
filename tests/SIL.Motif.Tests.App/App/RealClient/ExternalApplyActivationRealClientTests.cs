using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.Motif.App.Services;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ExternalApplyActivationRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public Task ReceiptWriteFailureIsShownAndBlockedWhenTheWorkspaceChecksActivation()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new WalkthroughProject(pristine);
            PendingChangeFixture.AddIncorrectSpelling(
                project.FwDataPath, project.ManagedRoot, "reconciliation-activation-word");
            var parser = FakeParser.Copy(project.ManagedRoot);
            await using var runner = new InProcessRunnerLauncher(
                new JobRunnerLaunchOptions(project.ManagedRoot, parser));
            var client = RealCommandClient.Create(project.ManagedRoot, parser, runner);
            await using var workspace = CreateWorkspace(client);

            await workspace.Context.OpenProjectAsync(project.FwDataPath);
            var review = workspace.PageModel<ReviewPageModel>();
            await review.MeasureCommand.ExecuteAsync(null);
            Assert.True(review.ApplyCommand.CanExecute(null), review.ApplyBlockReason);

            var before = ProjectSha256(project.FwDataPath);
            var outcome = await RunReconciliationFailureAsync(project.FwDataPath,
                IsolatedRunner.Process(project.ManagedRoot).Options);

            Assert.Equal(4, outcome.ExitCode);
            using var failure = JsonDocument.Parse(outcome.Error);
            Assert.Contains("applied and saved to the project", failure.RootElement.GetProperty("message").GetString());
            Assert.Contains("recording that in the proposal store failed",
                failure.RootElement.GetProperty("message").GetString());
            Assert.NotEqual(before, ProjectSha256(project.FwDataPath));
            Assert.True(ProjectReconciliationMarker.Exists(project.FwDataPath));

            await workspace.CheckFreshnessAsync();

            Assert.Equal("apply.reconciliation-needed", review.ApplyRefusal?.Code);
            Assert.Equal("Applying may have completed, but its result could not be confirmed. " +
                "Check the project before retrying.", review.ApplyRefusal?.Sentence);
            Assert.Equal(review.ApplyRefusal?.Sentence, review.ApplyBlockReason);
            Assert.False(review.CanApply);
            Assert.False(review.ApplyCommand.CanExecute(null));
            Assert.True(ProjectReconciliationMarker.Clear(project.FwDataPath));
        }, TimeSpan.FromMinutes(3));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public Task ReturningFromFieldWorksReloadsTheApplyOutcome(int expectedExitCode)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new WalkthroughProject(pristine);
            var parser = FakeParser.Copy(project.ManagedRoot);
            var cliRunner = IsolatedRunner.Process(project.ManagedRoot, parser);
            Guid analysisId = Guid.Empty;

            if (expectedExitCode == 2)
                AddApprovalChange(project, pristine, out analysisId);
            else
                PendingChangeFixture.AddIncorrectSpelling(
                    project.FwDataPath, project.ManagedRoot, "external-apply-word");

            var pending = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, "1.0"));
            Assert.True(pending.Succeeded, pending.Refusal?.Message);
            if (expectedExitCode is 0 or 3 or 4)
            {
                var change = Assert.Single(pending.Value!.Changes);
                var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
                        project.FwDataPath, pending.Value.DraftId!, pending.Value.Revision, [change.Word]),
                    new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: cliRunner);
                Assert.True(measured.Succeeded, measured.Refusal?.Message);
                Assert.True(measured.Value!.EvidenceComplete);
            }

            using var held = expectedExitCode == 3 ? new FieldWorksSimulator(project.FwDataPath).Hold() : null;
            await using var appRunner = new InProcessRunnerLauncher(
                new JobRunnerLaunchOptions(project.ManagedRoot, parser));
            var client = RealCommandClient.Create(project.ManagedRoot, parser, appRunner);
            await using var workspace = CreateWorkspace(client);
            await workspace.Context.OpenProjectAsync(project.FwDataPath);
            var review = workspace.PageModel<ReviewPageModel>();
            var originalBaselineToken = workspace.Baseline.Token;
            var originalFit = Assert.Single(workspace.Context.Changes.Snapshot.FitSummary);
            var before = expectedExitCode == 4 ? ProjectSha256(project.FwDataPath) : null;

            if (expectedExitCode == 2)
            {
                new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
                    NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    {
                        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                            .GetObject(analysisId);
                        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves);
                    }));
                var captured = BaselineCaptureCommand.Capture(
                    new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
                Assert.True(captured.Succeeded, captured.Refusal?.Message);
            }

            var outcome = expectedExitCode == 4
                ? await RunReconciliationFailureAsync(project.FwDataPath, cliRunner.Options)
                : expectedExitCode == 0
                    ? await RunApplyWithWorkerAsync(project.FwDataPath, cliRunner.Options)
                    : await CliProcess.RunAsync(CliProcess.Start(cliRunner.Options,
                        "apply", "--all-pending", "--project", project.FwDataPath, "--json"));
            Assert.True(outcome.ExitCode == expectedExitCode,
                $"Expected CLI exit {expectedExitCode}, got {outcome.ExitCode}. stderr: {outcome.Error} stdout: {outcome.Output}");

            if (expectedExitCode == 0)
            {
                Assert.Empty(outcome.Error);
                using var response = JsonDocument.Parse(outcome.Output);
                Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(response.RootElement.GetProperty("applied").GetBoolean());
            }
            else
            {
                Assert.Empty(outcome.Output);
                using var failure = JsonDocument.Parse(outcome.Error);
                var expectedCode = expectedExitCode switch
                {
                    2 => "apply.change-no-longer-fits",
                    3 => "project.in-use",
                    4 => "apply.reconciliation-needed",
                    _ => throw new ArgumentOutOfRangeException(nameof(expectedExitCode)),
                };
                Assert.Equal(expectedCode, failure.RootElement.GetProperty("code").GetString());
            }

            if (expectedExitCode == 4)
                Assert.NotEqual(before, ProjectSha256(project.FwDataPath));

            held?.Dispose();
            await workspace.CheckFreshnessAsync();

            switch (expectedExitCode)
            {
                case 0:
                    Assert.Empty(workspace.Context.Changes.Items);
                    break;
                case 2:
                    Assert.NotEqual(originalBaselineToken, workspace.Baseline.Token);
                    Assert.False(Assert.Single(workspace.Context.Changes.Items).Fit?.StillFits);
                    break;
                case 3:
                    Assert.False(workspace.Baseline.FieldWorksHeldProject);
                    var refreshedFit = Assert.Single(workspace.Context.Changes.Snapshot.FitSummary);
                    Assert.Equal(originalFit.ChangeId, refreshedFit.ChangeId);
                    Assert.Equal(originalFit.StillFits, refreshedFit.StillFits);
                    Assert.Equal(originalFit.Status, refreshedFit.Status);
                    Assert.Equal(originalFit.Reasons, refreshedFit.Reasons);
                    Assert.Single(workspace.Context.Changes.Items);
                    await review.MeasureCommand.ExecuteAsync(null);
                    Assert.True(review.ApplyCommand.CanExecute(null), review.ApplyBlockReason);
                    break;
                case 4:
                    Assert.Equal("apply.reconciliation-needed", review.ApplyRefusal?.Code);
                    Assert.False(review.ApplyCommand.CanExecute(null));
                    break;
            }
        }, TimeSpan.FromMinutes(3));
        return Task.CompletedTask;
    }

    private static void AddApprovalChange(
        WalkthroughProject project, PristineProjectFixture pristine, out Guid analysisId)
    {
        var word = "external-analysis-drift";
        var (wordformId, storedAnalysisId) = StoredAnalysisFixture.Add(
            project.FwDataPath, pristine.Seed.FirstEntryId, word);
        analysisId = storedAnalysisId;
        var captured = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, "1.0"));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var added = PendingChanges.Put(new PutPendingChangeRequest(
            project.FwDataPath, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "approve",
                CanonicalId.FromGuid(wordformId).Value, word,
                StoredAnalysisId: CanonicalId.FromGuid(analysisId).Value)));
        Assert.True(added.Succeeded, added.Refusal?.Message);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunApplyWithWorkerAsync(
        string projectPath, JobRunnerLaunchOptions runner)
    {
        var start = CliProcess.Start(runner, "apply", "--all-pending", "--project", projectPath, "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var worker = await CliProcess.TryStartQueuedWorkerAsync(projectPath, runner, process);
        await process.WaitForExitAsync();
        if (worker is not null) await worker.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunReconciliationFailureAsync(
        string projectPath, JobRunnerLaunchOptions runner)
    {
        var start = CliProcess.Start(runner, "apply", "--all-pending", "--project", projectPath, "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        start.Environment["MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR"] = projectPath;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var worker = await CliProcess.TryStartQueuedWorkerAsync(projectPath, runner, process);
        await process.WaitForExitAsync();
        if (worker is not null) await worker.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static WorkspaceShellViewModel CreateWorkspace(ICommandClient client)
    {
        var selection = new SelectionViewModel(client);
        return new WorkspaceShellViewModel(new ProjectViewModel(client, new ProjectPicker()),
            new BaselineViewModel(client), selection, new AssessViewModel(client, selection),
            new FolderPicker(), new DragSource(), client);
    }

    private static string ProjectSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class ProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }

}
