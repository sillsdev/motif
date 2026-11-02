using System.Security.Cryptography;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// A Trial or Review check after an Apply or a FieldWorks save, through the real command client, store and runner
/// with no fake of the Baseline refusal, on a disposable synthetic project.
/// </summary>
[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheTestCollection.Name)]
public sealed class RefreshBeforeCheckRealRefusalTests(PristineProjectFixture pristine)
{
    [Fact]
    public Task TryAgainAfterAnApplyRefreshesOnceAndTheTrialCompletesOnTheWindowsNewBaseline()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new WalkthroughProject(pristine);
            var proposalId = FinalizedProposal(project);
            await using var window = await OpenAppliedWindowAsync(project);
            var staleToken = window.Workspace.Baseline.Token;
            await AssertTheStoreHoldsNoCurrentBaselineAsync(window);

            var outcome = await window.Workspace.Context.TryAgainAsync(proposalId, CancellationToken.None);

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Null(window.Workspace.Context.TrialRun.StateFor(proposalId).Refusal);
            Assert.Equal(1, window.Refreshes);
            Assert.False(window.Workspace.Context.Evidence.AppliedSinceRefresh);
            Assert.NotEqual(staleToken, window.Workspace.Baseline.Token);
            var trial = outcome.Value!;
            Assert.Equal(JobStatus.Completed, trial.Status);
            Assert.Equal(window.Workspace.Baseline.Token, trial.Baseline);
            Assert.Equal(File.GetLastWriteTimeUtc(project.FwDataPath).Ticks,
                window.Workspace.Baseline.SourceLastWriteUtc!.Value.UtcDateTime.Ticks);
        }, TimeSpan.FromMinutes(3));
        return Task.CompletedTask;
    }

    // A Baseline captured outside the window is what lets a change be staged after the Apply.
    [Fact]
    public Task ReviewCheckAfterAnApplyRefreshesOnceAndMeasuresOnTheWindowsNewBaseline()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new WalkthroughProject(pristine);
            await using var window = await OpenAppliedWindowAsync(project);
            var staleToken = window.Workspace.Baseline.Token;
            var outside = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath),
                project.ManagedRoot);
            Assert.True(outside.Succeeded, outside.Refusal?.Message);
            await window.Workspace.Context.Changes.PutAsync(new ChangeIntent(CanonicalId.Mint().Value,
                "incorrect-spelling", CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value,
                SeededProject.AnalysedWordForm));
            Assert.Single(window.Workspace.Context.Changes.Items);
            Assert.True(window.Workspace.Context.Evidence.AppliedSinceRefresh);
            var review = window.Workspace.PageModel<ReviewPageModel>();

            await review.MeasureCommand.ExecuteAsync(null);

            Assert.Null(review.MeasurementRefusal);
            Assert.Equal(1, window.Refreshes);
            Assert.False(window.Workspace.Context.Evidence.AppliedSinceRefresh);
            Assert.NotEqual(staleToken, window.Workspace.Baseline.Token);
            Assert.True(review.ApplyCommand.CanExecute(null), review.ApplyBlockReason);
        }, TimeSpan.FromMinutes(3));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AReviewCheckAfterAFieldWorksSaveIsRefusedUntilTheWindowsRefreshCapturesTheSave()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new WalkthroughProject(pristine);
            PendingChangeFixture.AddIncorrectSpelling(project.FwDataPath, project.ManagedRoot, "refresh-before-check");
            var parser = FakeParser.Copy(project.ManagedRoot);
            var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(project.ManagedRoot, parser));
            await using var window = new AppliedWindow(
                RealCommandClient.Create(project.ManagedRoot, parser, runner), runner, project.FwDataPath);
            await window.Workspace.Context.OpenProjectAsync(project.FwDataPath);
            new FieldWorksSimulator(project.FwDataPath).AddWordform("saved-by-fieldworks");
            var review = window.Workspace.PageModel<ReviewPageModel>();

            await review.MeasureCommand.ExecuteAsync(null);

            Assert.Equal(RefusalCodes.ChangeRefreshRequired, review.MeasurementRefusal?.Code);
            Assert.Equal(0, window.Refreshes);

            await window.Workspace.RefreshCommand.ExecuteAsync(null);
            await review.MeasureCommand.ExecuteAsync(null);

            Assert.Null(review.MeasurementRefusal);
            Assert.Equal(1, window.Refreshes);
            Assert.True(review.ApplyCommand.CanExecute(null), review.ApplyBlockReason);
        }, TimeSpan.FromMinutes(3));
        return Task.CompletedTask;
    }

    // After the person's Apply the project was saved past its Baseline and the store reports none as current.
    private static async Task AssertTheStoreHoldsNoCurrentBaselineAsync(AppliedWindow window)
    {
        var current = await window.Client.GetCurrentBaselineAsync(
            new CurrentBaselineRequest(window.ProjectPath), CancellationToken.None);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        Assert.Null(current.Value!.Token);
        Assert.Equal(0, window.Refreshes);
    }

    // The person's own Apply from Review, through the real client, on a disposable copy of a synthetic project.
    private async Task<AppliedWindow> OpenAppliedWindowAsync(WalkthroughProject project)
    {
        PendingChangeFixture.AddIncorrectSpelling(project.FwDataPath, project.ManagedRoot, "refresh-before-check");
        var parser = FakeParser.Copy(project.ManagedRoot);
        var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(project.ManagedRoot, parser));
        var client = RealCommandClient.Create(project.ManagedRoot, parser, runner);
        var window = new AppliedWindow(client, runner, project.FwDataPath);
        try
        {
            await window.Workspace.Context.OpenProjectAsync(project.FwDataPath);
            var review = window.Workspace.PageModel<ReviewPageModel>();
            await review.MeasureCommand.ExecuteAsync(null);
            Assert.True(review.ApplyCommand.CanExecute(null), review.ApplyBlockReason);
            var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project.FwDataPath)));

            await review.ApplyCommand.ExecuteAsync(null);

            Assert.Null(review.ApplyRefusal);
            Assert.NotEqual(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project.FwDataPath))));
            Assert.True(window.Workspace.Context.Evidence.AppliedSinceRefresh);
            return window;
        }
        catch
        {
            await window.DisposeAsync();
            throw;
        }
    }

    private string FinalizedProposal(WalkthroughProject project)
    {
        const string draftName = "refresh before check";
        var version = MotifProductVersion.CurrentText;
        var created = ProposalCommands.New(new NewDraftRequest(project.FwDataPath, version, draftName, draftName));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(project.FwDataPath, version, draftName,
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, "en", "refresh before check"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        DraftRationale.Author(project.FwDataPath, draftName, draftName, "Why this gloss is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(project.FwDataPath, version, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return finalized.Value!.ProposalId;
    }

    private sealed class AppliedWindow : IAsyncDisposable
    {
        private readonly InProcessRunnerLauncher _runner;

        public AppliedWindow(CommandClient client, InProcessRunnerLauncher runner, string projectPath)
        {
            Client = client;
            _runner = runner;
            ProjectPath = projectPath;
            var selection = new SelectionViewModel(client);
            Workspace = new WorkspaceShellViewModel(new ProjectViewModel(client, new NoProjectPicker()),
                new BaselineViewModel(client), selection, new AssessViewModel(client, selection),
                new NoFolderPicker(), new NoDragSource(), client);
            Workspace.Baseline.Refreshed += (_, _) => Refreshes++;
        }

        public CommandClient Client { get; }

        public string ProjectPath { get; }

        public WorkspaceShellViewModel Workspace { get; }

        public int Refreshes { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Workspace.DisposeAsync();
            await _runner.DisposeAsync();
        }
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
