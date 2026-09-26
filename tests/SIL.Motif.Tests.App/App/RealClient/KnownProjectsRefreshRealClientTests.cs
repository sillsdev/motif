using Avalonia.Input;
using SIL.LCModel;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class KnownProjectsRefreshRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CapturingAProjectMakesItAvailableToOpenRecentInTheSameSession()
    {
        using var capturedProject = new WalkthroughProject(pristine);
        using var otherProject = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(capturedProject.ManagedRoot);
        var commands = RealCommandClient.Create(capturedProject.ManagedRoot, parserPath);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var selection = new SelectionViewModel(commands);
            await using var workspace = new WorkspaceShellViewModel(
                new ProjectViewModel(commands, new FixedProjectPicker(capturedProject.FwDataPath)),
                new BaselineViewModel(commands), selection, new AssessViewModel(commands, selection),
                new NoFolderPicker(), new NoDragSource(), commands);

            await workspace.SetProjectAsync(capturedProject.FwDataPath);
            Assert.Empty(workspace.Project.KnownProjects);

            await workspace.RefreshCommand.ExecuteAsync(null);

            Assert.True(workspace.Baseline.HasBaseline);
            Assert.Single(workspace.Project.KnownProjects, project =>
                string.Equals(project.FullFwDataPath, capturedProject.FwDataPath,
                    StringComparison.OrdinalIgnoreCase));

            await workspace.SetProjectAsync(otherProject.FwDataPath);

            Assert.Single(workspace.RecentProjects, project =>
                string.Equals(project.FullFwDataPath, capturedProject.FwDataPath,
                    StringComparison.OrdinalIgnoreCase));
        }, TimeSpan.FromMinutes(2));
    }

    private sealed class FixedProjectPicker(string projectPath) : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(projectPath);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
