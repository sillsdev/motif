using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the Grow/Polish stage plumbing (ADR 0100): a stage hides pages live, and is kept per user.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StagePlumbingTests
{
    // Parsimony stands in for a Polish-only page; every other entry shows in both stages as in production.
    private static readonly IReadOnlyList<PageEntry> Entries =
    [
        .. PageRegistry.Entries.Select(entry => entry.Page == WorkspacePage.Parsimony
            ? entry with { Stages = [MotifStage.Polish] }
            : entry),
    ];

    private static WorkspaceShellViewModel NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        return new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new NoPicker()), new BaselineViewModel(fake), selection,
            new AssessViewModel(fake, selection), new NoPicker(), new NoPicker(), fake, pageEntries: Entries);
    }

    [Fact]
    public void ARegistryEntryShowsInBothStagesUnlessItSaysOtherwise()
    {
        Assert.All(PageRegistry.Entries, entry =>
        {
            Assert.True(entry.ShowsIn(MotifStage.Grow));
            Assert.True(entry.ShowsIn(MotifStage.Polish));
        });
    }

    [Fact]
    public void APolishOnlyPageLeavesTheRailInGrowAndComesBack()
    {
        var workspace = NewWorkspace();
        Assert.Equal(MotifStage.Polish, workspace.Context.Stage);
        Assert.Contains(workspace.VisiblePages, page => page.Page == WorkspacePage.Parsimony);
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        workspace.Context.Stage = MotifStage.Grow;

        Assert.DoesNotContain(workspace.VisiblePages, page => page.Page == WorkspacePage.Parsimony);
        Assert.Contains(nameof(WorkspaceShellViewModel.VisiblePages), changes);
        Assert.False(workspace.Context.CanOpenPage(WorkspacePage.Parsimony));
        workspace.Context.OpenPage(WorkspacePage.Parsimony);
        Assert.NotEqual(WorkspacePage.Parsimony, workspace.CurrentPage);

        workspace.Context.Stage = MotifStage.Polish;

        Assert.Contains(workspace.VisiblePages, page => page.Page == WorkspacePage.Parsimony);
        Assert.True(workspace.Context.CanOpenPage(WorkspacePage.Parsimony));
    }

    [Fact]
    public void TheSelectedPageStaysWhenStillVisibleAndOtherwiseFallsBackToOverview()
    {
        var workspace = NewWorkspace();
        workspace.CurrentPage = WorkspacePage.Texts;
        workspace.Context.Stage = MotifStage.Grow;
        Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);

        workspace.Context.Stage = MotifStage.Polish;
        workspace.CurrentPage = WorkspacePage.Parsimony;
        workspace.Context.Stage = MotifStage.Grow;

        Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
        Assert.Equal(WorkspacePage.Overview, workspace.SelectedPage.Page);
    }

    [Fact]
    public void TheStageRoundTripsThroughPreferencesAndDefaultsToPolish()
    {
        var folder = Directory.CreateTempSubdirectory("motif-stage-").FullName;
        try
        {
            var path = Path.Combine(folder, "user-preferences.json");
            var store = new FileUserPreferencesStore(path);
            Assert.Equal(MotifStage.Polish, store.Current.Stage);

            var workspace = NewWorkspace();
            workspace.Context.UseStagePreferences(store);
            workspace.Context.Stage = MotifStage.Grow;

            var reread = new FileUserPreferencesStore(path);
            Assert.Equal(PreferenceState.Ready, reread.State);
            Assert.Equal(MotifStage.Grow, reread.Current.Stage);
            Assert.Equal(store.Current, reread.Current);

            var next = NewWorkspace();
            next.Context.UseStagePreferences(reread);
            Assert.Equal(MotifStage.Grow, next.Context.Stage);
            Assert.DoesNotContain(next.VisiblePages, page => page.Page == WorkspacePage.Parsimony);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class NoPicker : IProjectPicker, IHandoffFolderPicker, IFileDragSource
    {
        Task<string?> IProjectPicker.PickProjectFileAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        Task<string?> IHandoffFolderPicker.PickFolderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        Task<DragDropEffects> IFileDragSource.StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
