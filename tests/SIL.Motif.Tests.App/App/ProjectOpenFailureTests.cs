using System.ComponentModel;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProjectOpenFailureTests
{
    private const string ProjectA = @"C:\projects\one.fwdata";
    private const string ProjectB = @"C:\projects\two.fwdata";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OpenRefusal = "Motif could not open this project.";

    [Fact]
    public async Task AFailureWhileOpeningShowsARefusalAndKeepsThePickerUsable()
    {
        var fake = new FakeCommandClient();
        var picker = new FakeProjectPicker { PathToReturn = ProjectA };
        var token = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);
        fake.OnGetCurrentBaseline((_, _) =>
            Task.FromException<CommandOutcome<CurrentBaselineResponse>>(new InvalidOperationException("The project could not be read.")));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, picker), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection), new FakeFolderPicker(), new FakeDragSource(), fake);
        var refusalShown = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clearedBindings = new HashSet<string>();
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
        workspace.PropertyChanged += (_, args) =>
        {
            if (workspace.OpenRefusal is null &&
                args.PropertyName is nameof(WorkspaceShellViewModel.OpenRefusal) or
                    nameof(WorkspaceShellViewModel.HasOpenRefusal))
                clearedBindings.Add(args.PropertyName);
        };

        await workspace.Project.BrowseCommand.ExecuteAsync(null);
        var refusal = await refusalShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(OpenRefusal, refusal);
        Assert.Equal("The project could not be read.", workspace.OpenRefusal!.Details);
        Assert.False(workspace.HasRefreshRefusal);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
        picker.PathToReturn = ProjectB;
        clearedBindings.Clear();

        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(ProjectB, workspace.Context.ProjectPath);
        Assert.Contains(nameof(WorkspaceShellViewModel.OpenRefusal), clearedBindings);
        Assert.Contains(nameof(WorkspaceShellViewModel.HasOpenRefusal), clearedBindings);

        void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(WorkspaceShellViewModel.OpenRefusal) &&
                workspace.OpenRefusal is { } shown)
                refusalShown.TrySetResult(shown.Sentence);
        }
    }

    [Fact]
    public async Task AFailureWhileOpeningFromOpenRecentShowsARefusal()
    {
        var fake = new FakeCommandClient();
        var token = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);
        fake.OnGetCurrentBaseline((_, _) => Task.FromException<CommandOutcome<CurrentBaselineResponse>>(
            new InvalidOperationException("The project could not be read.")));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new FakeFolderPicker(), new FakeDragSource(), fake);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(ProjectA));

        Assert.Equal(OpenRefusal, workspace.OpenRefusal!.Sentence);
        Assert.Equal("The project could not be read.", workspace.OpenRefusal.Details);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(ProjectB));
        Assert.Equal(ProjectB, workspace.Context.ProjectPath);
    }

    [Fact]
    public async Task ABackupIsRestoredAndItsRestoredCopyOpens()
    {
        const string backup = @"C:\projects\one 2026-10-07.fwbackup";
        var fake = new FakeCommandClient();
        var token = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
        fake.RestoreBackupIs(_ => CommandOutcome<RestoredBackup>.Success(new RestoredBackup(ProjectA)));
        var workspace = NewWorkspace(fake);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(backup));

        Assert.Equal([backup], fake.RestoredBackups);
        Assert.Equal(ProjectA, workspace.Context.ProjectPath);
        Assert.Null(workspace.OpenRefusal);
    }

    [Fact]
    public async Task ABackupThatCannotBeRestoredShowsItsRefusalAndOpensNothing()
    {
        var fake = new FakeCommandClient();
        fake.RestoreBackupIs(_ => CommandOutcome<RestoredBackup>.Refused(new Refusal(
            RefusalCodes.ProjectBackupUnreadable, FailureReason.InvalidArgument, "Not a FieldWorks backup.")));
        var workspace = NewWorkspace(fake);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(@"C:\projects\notes.fwbackup"));

        Assert.Equal(RefusalCodes.ProjectBackupUnreadable, workspace.OpenRefusal!.Code);
        Assert.Equal("Motif cannot restore that file. Choose a .fwbackup file that FieldWorks made.",
            workspace.OpenRefusal.Sentence);
        Assert.False(workspace.HasProject);
    }

    private static WorkspaceShellViewModel NewWorkspace(FakeCommandClient fake)
    {
        var selection = new SelectionViewModel(fake);
        return new WorkspaceShellViewModel(new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new FakeFolderPicker(), new FakeDragSource(), fake);
    }

    [Fact]
    public async Task AParticipantCancellationShowsAnOpenRefusal()
    {
        var fake = new FakeCommandClient();
        fake.OnGetCurrentBaseline((_, _) => Task.FromException<CommandOutcome<CurrentBaselineResponse>>(
            new OperationCanceledException("The project read was cancelled.")));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new FakeFolderPicker(), new FakeDragSource(), fake);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(ProjectA));

        Assert.Equal(OpenRefusal, workspace.OpenRefusal!.Sentence);
        Assert.False(workspace.HasProject);
    }

    [Fact]
    public async Task AFailedOpenLeavesNothingOfEitherProjectActionable()
    {
        var fake = new FakeCommandClient();
        var token = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);
        fake.OnGetCurrentBaseline((request, _) => request.ProjectPath == ProjectB
            ? Task.FromException<CommandOutcome<CurrentBaselineResponse>>(
                new InvalidOperationException("The project could not be read."))
            : Task.FromResult(CommandOutcome<CurrentBaselineResponse>.Success(
                new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false))));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new FakeFolderPicker(), new FakeDragSource(), fake);
        var handoff = workspace.PageModel<AiHandoffPageModel>().Handoff;
        var statistics = workspace.PageModel<TimingPageModel>().Statistics;

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(ProjectA));
        Assert.Equal(ProjectA, handoff.ProjectPath);
        Assert.Equal(ProjectA, statistics.ProjectPath);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(ProjectB));

        Assert.NotNull(workspace.OpenRefusal);
        Assert.Null(handoff.ProjectPath);
        Assert.Null(statistics.ProjectPath);
        Assert.False(workspace.HasProject);
        Assert.False(workspace.RefreshCommand.CanExecute(null));
        Assert.Null(workspace.Project.OpenUnlistedPath);
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
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
