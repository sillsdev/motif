using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Delete this file and reopen appears only for a store another version of Motif made, and pressing it asks
/// before anything is deleted.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StoreDeletionButtonTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private const string StorePath = @"C:\projects\one.motif.db";
    private const string DeleteButtonText = "Delete this file and reopen";

    private readonly AvaloniaHeadlessFixture _avalonia;

    public StoreDeletionButtonTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Theory]
    [InlineData(RefusalCodes.StoreOtherVersion, true)]
    [InlineData(RefusalCodes.StoreUnsupported, false)]
    [InlineData(RefusalCodes.StoreInconsistent, false)]
    [InlineData(RefusalCodes.ProjectStoreIo, false)]
    public void TheButtonAppearsOnlyForAStoreFromAnotherVersion(string code, bool offered)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineRefusesWith(new Refusal(code, FailureReason.Refused, "The store was refused.",
            new Dictionary<string, string> { ["fwDataPath"] = Project, [RefusalFactNames.StorePath] = StorePath }));

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var window = new MainWindow();
            var selection = new SelectionViewModel(fake);
            var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new NoProjectPicker()),
                new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
                new NoFolderPicker(), new NoDragSource(), fake, clipboard: new AvaloniaClipboard(window));
            window.Compose(workspace);
            try
            {
                window.Show();
                await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(Project));
                Settle(window);

                var block = window.GetLogicalDescendants().OfType<RefusalBlock>()
                    .Single(candidate => AutomationProperties.GetName(candidate) == "Refresh refusal");
                Assert.True(block.IsEffectivelyVisible);
                var button = DeleteButton(block);
                Assert.Equal(offered, button.IsEffectivelyVisible);
                if (!offered) return;

                var question = window.GetLogicalDescendants().OfType<StackPanel>()
                    .Single(panel => panel.Name == "StoreDeletionQuestion");
                Assert.False(question.IsEffectivelyVisible);
                Assert.True(button.IsEffectivelyEnabled);

                button.Command!.Execute(button.CommandParameter);
                Settle(window);

                Assert.True(question.IsEffectivelyVisible);
                var shown = question.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
                Assert.Contains(WorkspaceShellViewModel.StoreDeletionWarning, shown);
                var choices = question.GetLogicalDescendants().OfType<Button>()
                    .Where(choice => choice.IsEffectivelyVisible).Select(choice => choice.Content).ToList();
                Assert.Equal(["Delete and reopen", "Cancel"], choices);
                Assert.Empty(fake.DeleteRefusedStoreRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASurfaceThatCannotReopenTheProjectShowsNoButton()
    {
        _avalonia.Invoke(() =>
        {
            var block = new RefusalBlock { DataContext = WindowRefusal.From(StoreDeletionFlowTests.OtherVersionRefusal()) };
            var window = new Window { Content = block, Width = 600, Height = 400 };
            try
            {
                window.Show();
                Settle(window);

                Assert.True(block.IsEffectivelyVisible);
                Assert.False(DeleteButton(block).IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Button DeleteButton(RefusalBlock block) =>
        block.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, DeleteButtonText));

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
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
