using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class RecentProjectRowTests
{
    [Fact]
    public void ProjectNameUsesWindowsPathSeparatorsOnUnix()
    {
        var recent = new RecentProjectViewModel(
            @"C:\Users\linguist\FieldWorks\Projects\Sample\Sample.fwdata");

        Assert.Equal("Sample", recent.Name);
    }

    [Fact]
    public async Task TheChooserKeepsTheNewestCopyAndShowsItsStoredFieldsAndFolderLink()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MotifSyntheticTurkic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var olderPath = Path.Combine(folder, "older.fwdata");
        var recentPath = Path.Combine(folder, "MotifSyntheticTurkic.fwdata");
        var identity = "11111111-2222-4333-8444-555555555555";
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs(
        [
            new KnownProjectSummary(olderPath, DateTimeOffset.UtcNow.AddDays(-1))
            {
                ProjectIdentity = identity,
                StateText = "Not parsed yet",
                WordformCount = 91,
            },
            new KnownProjectSummary(recentPath, DateTimeOffset.UtcNow)
            {
                ProjectIdentity = identity,
                StateText = "Changes not applied yet",
                WordformCount = 104,
            },
        ]);
        var launcher = new RecordingLauncher();
        var workspace = NewWorkspace(fake, launcher);

        try
        {
            await workspace.Project.LoadKnownProjectsAsync();
            var recent = Assert.Single(workspace.PageModel<OverviewPageModel>().ChooseProjectRows);

            Assert.Equal(recentPath, recent.FullFwDataPath);
            Assert.Equal(identity, recent.ProjectIdentity);
            Assert.Equal("Changes not applied yet", recent.StateText);
            Assert.Equal(104, recent.WordformCount);
            Assert.Equal("104 wordforms", recent.WordformCountText);
            Assert.True(recent.CanOpenFolder);
            Assert.True(workspace.OpenRecentProjectFolderCommand.CanExecute(recent));

            await workspace.OpenRecentProjectFolderCommand.ExecuteAsync(recent);

            Assert.Equal(new Uri(folder), launcher.LastUri);
        }
        finally
        {
            await workspace.DisposeAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ARecentProjectWithAMissingFolderHasNoWorkingFolderLink()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs([new KnownProjectSummary(Path.Combine(missingFolder, "one.fwdata"), DateTimeOffset.UtcNow)]);
        var launcher = new RecordingLauncher();
        var workspace = NewWorkspace(fake, launcher);

        try
        {
            await workspace.Project.LoadKnownProjectsAsync();
            var recent = Assert.Single(workspace.PageModel<OverviewPageModel>().ChooseProjectRows);

            Assert.False(recent.CanOpenFolder);
            Assert.False(workspace.OpenRecentProjectFolderCommand.CanExecute(recent));
            await workspace.OpenRecentProjectFolderCommand.ExecuteAsync(recent);
            Assert.Null(launcher.LastUri);
        }
        finally
        {
            await workspace.DisposeAsync();
        }
    }

    [Fact]
    public void CopiesOfOneIdentityCompareCaseInsensitivelyOnEveryPlatform()
    {
        const string identity = "ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF";
        var paths = new[]
        {
            new KnownProjectSummary(Path.Combine(Path.GetTempPath(), "older.fwdata"), DateTimeOffset.UtcNow.AddDays(-1))
                { ProjectIdentity = identity },
            new KnownProjectSummary(Path.Combine(Path.GetTempPath(), "newer.fwdata"), DateTimeOffset.UtcNow)
                { ProjectIdentity = identity.ToLowerInvariant() },
        };
        var recent = Assert.Single(RecentProjectViewModel.MostRecentlySeenCopies(paths));
        Assert.Equal(paths[1].FullFwDataPath, recent.FullFwDataPath);
    }

    [Fact]
    public async Task RecentCopiesOfTheOpenProjectAreExcludedByTheirIdentity()
    {
        const string identity = "abcdefab-cdef-4abc-8def-abcdefabcdef";
        var openPath = Path.Combine(Path.GetTempPath(), "open-project.fwdata");
        var copyPath = Path.Combine(Path.GetTempPath(), "other-copy.fwdata");
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs([new KnownProjectSummary(copyPath, DateTimeOffset.UtcNow)
            { ProjectIdentity = identity.ToUpperInvariant() }]);
        var token = new SIL.Motif.Contract.Baselines.BaselineToken(identity,
            "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));
        var saved = DateTimeOffset.Parse("2026-09-05T10:58:00Z");
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, saved, false)
            { ProjectLastWriteUtc = saved });
        var workspace = NewWorkspace(fake, new RecordingLauncher());
        try
        {
            await workspace.Project.LoadKnownProjectsAsync();
            await workspace.SetProjectAsync(openPath, captureFreshProject: false);
            Assert.Equal(openPath, workspace.Context.ProjectPath);
            Assert.Empty(workspace.RecentProjects);
        }
        finally { await workspace.DisposeAsync(); }
    }

    [Fact]
    public async Task CopiesOfTheOpenProjectAreExcludedWithoutABaselineToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "MotifRecentWithoutBaseline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var identity = Guid.NewGuid();
        var path = Path.Combine(root, "open.fwdata");
        File.WriteAllText(path, $"<languageproject><rt class=\"LangProject\" guid=\"{identity:D}\" /></languageproject>");
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs([new KnownProjectSummary(Path.Combine(root, "copy.fwdata"), DateTimeOffset.UtcNow)
            { ProjectIdentity = identity.ToString("D").ToUpperInvariant() }]);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, default, false));
        var workspace = NewWorkspace(fake, new RecordingLauncher());
        try
        {
            await workspace.Project.LoadKnownProjectsAsync();
            await workspace.SetProjectAsync(path, captureFreshProject: false);
            Assert.Null(workspace.Context.Baseline?.Token);
            Assert.Empty(workspace.RecentProjects);
        }
        finally
        {
            await workspace.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkspaceShellViewModel NewWorkspace(FakeCommandClient fake, IUriLauncher launcher)
    {
        var selection = new SelectionViewModel(fake);
        return new WorkspaceShellViewModel(new ProjectViewModel(fake, new ProjectPicker()), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection), new FolderPicker(), new DragSource(), fake,
            uriLauncher: launcher);
    }

    private sealed class RecordingLauncher : IUriLauncher
    {
        public Uri? LastUri { get; private set; }

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            LastUri = uri;
            return Task.FromResult(true);
        }
    }

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
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
