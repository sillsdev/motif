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

/// <summary>
/// A store made by another version of Motif is deleted only after the person confirms, and only for the project
/// on screen; the project then opens again, so Motif recreates the store.
/// </summary>
public sealed class StoreDeletionFlowTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private const string StorePath = @"C:\projects\one.motif.db";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task DeletingAsksFirstAndCancelDeletesNothing()
    {
        var (fake, workspace) = await OpenRefusedProject();
        var refusal = workspace.Baseline.ShownRefusal!;

        Assert.True(workspace.DeleteRefusedStoreCommand.CanExecute(refusal));
        workspace.DeleteRefusedStoreCommand.Execute(refusal);

        Assert.True(workspace.IsConfirmingStoreDeletion);
        Assert.Equal("Changes not applied yet are lost. Your FieldWorks project is not touched.",
            WorkspaceShellViewModel.StoreDeletionWarning);
        Assert.Empty(fake.DeleteRefusedStoreRequests);

        workspace.CancelStoreDeletionCommand.Execute(null);

        Assert.False(workspace.IsConfirmingStoreDeletion);
        Assert.Empty(fake.DeleteRefusedStoreRequests);
        Assert.Equal(refusal, workspace.Baseline.ShownRefusal);
    }

    [Fact]
    public async Task ConfirmingDeletesThatProjectsStoreAndReopensIt()
    {
        var (fake, workspace) = await OpenRefusedProject();
        var token = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);
        fake.OnDeleteRefusedStore((request, _) =>
        {
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
            return Task.FromResult(CommandOutcome<ProjectStoreResetResponse>.Success(
                new ProjectStoreResetResponse(StorePath, true)));
        });
        var readsBefore = fake.CurrentBaselineRequests.Count;

        workspace.DeleteRefusedStoreCommand.Execute(workspace.Baseline.ShownRefusal);
        await workspace.ConfirmStoreDeletionCommand.ExecuteAsync(null);

        Assert.Equal(Project, Assert.Single(fake.DeleteRefusedStoreRequests).ProjectPath);
        Assert.False(workspace.IsConfirmingStoreDeletion);
        Assert.True(fake.CurrentBaselineRequests.Count > readsBefore);
        Assert.Null(workspace.Baseline.ShownRefusal);
        Assert.False(workspace.HasRefreshRefusal);
        Assert.Equal(token, workspace.Baseline.Token);
        Assert.Equal(Project, workspace.Context.ProjectPath);
    }

    [Fact]
    public async Task AStoreThatCannotBeDeletedSaysSoAndDoesNotReopen()
    {
        var (fake, workspace) = await OpenRefusedProject();
        fake.OnDeleteRefusedStore((_, _) => Task.FromResult(CommandOutcome<ProjectStoreResetResponse>.Refused(
            new Refusal(RefusalCodes.ProjectStoreIo, FailureReason.Refused, "The file is in use.",
                new Dictionary<string, string> { [RefusalFactNames.StorePath] = StorePath }))));
        var readsBefore = fake.CurrentBaselineRequests.Count;

        workspace.DeleteRefusedStoreCommand.Execute(workspace.Baseline.ShownRefusal);
        await workspace.ConfirmStoreDeletionCommand.ExecuteAsync(null);

        Assert.Equal(RefusalCodes.ProjectStoreIo, workspace.OpenRefusal!.Code);
        Assert.Equal(readsBefore, fake.CurrentBaselineRequests.Count);
        Assert.Equal(RefusalCodes.StoreOtherVersion, workspace.Baseline.ShownRefusal!.Code);
    }

    [Fact]
    public async Task OnlyAStoreFromAnotherVersionOffersDeletion()
    {
        var (_, workspace) = await OpenRefusedProject();
        var unsupported = WindowRefusal.From(new Refusal(RefusalCodes.StoreUnsupported, FailureReason.Refused,
            "Unsupported.", new Dictionary<string, string> { [RefusalFactNames.StorePath] = StorePath }));
        var pathless = WindowRefusal.From(new Refusal(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
            "Other version.", new Dictionary<string, string>()));

        Assert.False(workspace.DeleteRefusedStoreCommand.CanExecute(unsupported));
        Assert.False(workspace.DeleteRefusedStoreCommand.CanExecute(pathless));
        Assert.False(workspace.DeleteRefusedStoreCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningAnotherProjectDropsTheQuestion()
    {
        var (fake, workspace) = await OpenRefusedProject();
        workspace.DeleteRefusedStoreCommand.Execute(workspace.Baseline.ShownRefusal);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(@"C:\projects\two.fwdata"));

        Assert.False(workspace.IsConfirmingStoreDeletion);
        Assert.False(workspace.ConfirmStoreDeletionCommand.CanExecute(null));
        Assert.Empty(fake.DeleteRefusedStoreRequests);
    }

    internal static Refusal OtherVersionRefusal() => new(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
        "The Motif database is schema 1, but this build requires exactly schema 2.",
        new Dictionary<string, string> { ["fwDataPath"] = Project, [RefusalFactNames.StorePath] = StorePath });

    private static async Task<(FakeCommandClient Fake, WorkspaceShellViewModel Workspace)> OpenRefusedProject()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineRefusesWith(OtherVersionRefusal());
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new NoProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new NoFolderPicker(), new NoDragSource(), fake);
        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(Project));
        Assert.Equal(RefusalCodes.StoreOtherVersion, workspace.Baseline.ShownRefusal?.Code);
        return (fake, workspace);
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
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
