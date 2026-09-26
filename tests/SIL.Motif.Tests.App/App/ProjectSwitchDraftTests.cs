using Avalonia.Input;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ProjectSwitchDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task SwitchingAwayAndBackKeepsTheStoredDraft()
    {
        string projectA;
        string projectB;
        using (var cache = pristine.NewScratch())
        {
            projectA = cache.ProjectId.Path;
            AddWordform(cache, "stored-word-a");
            new FwDataProjectLoader().Save(cache);
        }
        using (var cache = pristine.NewScratch())
        {
            projectB = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        var managedRoot = Path.Combine(Path.GetTempPath(), "Motif.ProjectSwitch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);
        try
        {
            Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectA), managedRoot).Succeeded);
            Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectB), managedRoot).Succeeded);
            var client = new CommandClient(managedRoot);
            var selection = new SelectionViewModel(client);
            var workspace = new HandoffWorkspaceViewModel(new ProjectViewModel(client, new FakeProjectPicker()),
                new BaselineViewModel(client), selection, new AssessViewModel(client, selection),
                new FakeFolderPicker(), new FakeDragSource(), client);
            await workspace.SetProjectAsync(projectA);
            await workspace.Context.Changes.PutAsync(new ChangeIntent(
                "change-a", ChangeKinds.IncorrectSpelling, "", "stored-word-a"));
            Assert.Equal("change-a", Assert.Single(workspace.Context.Changes.Items).ChangeId);

            await workspace.SetProjectAsync(projectB);
            Assert.Empty(workspace.Context.Changes.Items);
            await workspace.SetProjectAsync(projectA);
            Assert.Equal("change-a", Assert.Single(workspace.Context.Changes.Items).ChangeId);

            await workspace.SetProjectAsync(projectB + ".missing");
            Assert.NotNull(workspace.Baseline.RefusalMessage);
            await workspace.SetProjectAsync(projectA);
            Assert.Equal("change-a", Assert.Single(workspace.Context.Changes.Items).ChangeId);

            var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = new BlockingOpenPageModel(workspace.Context, openStarted, finishOpen);
            using var cancellation = new CancellationTokenSource();
            var openingB = workspace.Context.OpenProjectAsync(projectB, cancellation.Token);
            await openStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(projectB, workspace.Context.ProjectPath);
            cancellation.Cancel();
            finishOpen.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => openingB);
            await workspace.SetProjectAsync(projectA);

            var reopened = new ChangesViewModel(client);
            await reopened.OpenProjectAsync(projectA);
            Assert.Equal("change-a", Assert.Single(reopened.Items).ChangeId);
        }
        finally
        {
            Directory.Delete(managedRoot, recursive: true);
        }
    }

    private static void AddWordform(LcmCache cache, string form) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)));

    private sealed class BlockingOpenPageModel(
        WorkspaceContext context, TaskCompletionSource started, TaskCompletionSource release) : PageModel(context)
    {
        protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await release.Task;
        }
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
