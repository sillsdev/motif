using System.ComponentModel;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
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

    [Fact]
    public async Task AFailureWhileOpeningShowsARefusalAndKeepsThePickerUsable()
    {
        var fake = new FakeCommandClient();
        var picker = new FakeProjectPicker { PathToReturn = ProjectA };
        var token = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);
        fake.OnGetCurrentBaseline((_, _) =>
            Task.FromException<CommandOutcome<CurrentBaselineResponse>>(new InvalidOperationException("The project could not be read.")));
        var selection = new SelectionViewModel(fake);
        var workspace = new HandoffWorkspaceViewModel(new ProjectViewModel(fake, picker), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection), new FakeFolderPicker(), new FakeDragSource(), fake);
        var refusalShown = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Baseline.PropertyChanged += OnBaselinePropertyChanged;

        await workspace.Project.BrowseCommand.ExecuteAsync(null);
        var refusal = await refusalShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("The project could not be read.", refusal);
        Assert.True(workspace.HasRefreshRefusal);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
        picker.PathToReturn = ProjectB;

        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(ProjectB, workspace.Context.ProjectPath);

        void OnBaselinePropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(BaselineViewModel.RefusalMessage) &&
                workspace.Baseline.RefusalMessage is { } message)
                refusalShown.TrySetResult(message);
        }
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
