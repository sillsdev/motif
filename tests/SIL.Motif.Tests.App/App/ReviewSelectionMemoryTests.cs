using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the Review page returning to the change (by its change id) the person last selected.</summary>
public sealed class ReviewSelectionMemoryTests
{
    private const string ProjectPath = @"C:\projects\markings.fwdata";

    [Fact]
    public async Task ASelectedChangeReturnsAfterReopeningTheProjectAndIsMarkedCurrent()
    {
        var store = new MemoryUserPreferencesStore();
        var first = await OpenAsync(store, ["a", "b", "c"]);
        var chosen = first.Entries[^1];
        first.SelectEntry(chosen);

        var second = await OpenAsync(store, ["a", "b", "c"]);

        Assert.Equal(chosen.Key, second.Current?.Key);
        Assert.True(second.Current?.IsCurrent);
    }

    [Fact]
    public async Task AChangeThatIsGoneKeepsTheFirstEntry()
    {
        var store = new MemoryUserPreferencesStore();
        var first = await OpenAsync(store, ["a", "b", "c"]);
        first.SelectEntry(first.Entries[^1]);

        var second = await OpenAsync(store, ["x", "y"]);

        Assert.Same(second.Entries[0], second.Current);
    }

    [Fact]
    public async Task ChangingTheFilterDoesNotReplaceTheRememberedChange()
    {
        var store = new MemoryUserPreferencesStore();
        var first = await OpenAsync(store, ["a", "b", "c"]);
        var chosen = first.Entries[^1];
        first.SelectEntry(chosen);
        first.SelectedFilterIndex = 1;
        first.SelectedFilterIndex = 0;

        var second = await OpenAsync(store, ["a", "b", "c"]);

        Assert.Equal(chosen.Key, second.Current?.Key);
    }

    private static async Task<ReviewPageModel> OpenAsync(IUserPreferencesStore store, string[] ids)
    {
        var changes = ids.Select(id => new PendingChange(id, "wordform/" + id, id, ChangeKinds.Approve, null, id,
            ["operation/" + id])
        {
            Analyses = [new ReviewAnalysis(new ParserReading([]), ReadingGrade.Candidate, true, true)],
            OriginPage = WorkspacePage.Texts.ToString(),
            StoredAnalysisId = "analysis/" + id,
        }).ToArray();
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", changes,
            changes.Select(change => new ChangeFit(change.ChangeId, ChangeFitStatus.Fits, [])).ToArray()));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        context.ViewMemory.UseStore(store);
        context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        return page;
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
