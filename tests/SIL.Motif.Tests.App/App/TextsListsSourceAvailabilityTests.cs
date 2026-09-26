using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsListsSourceAvailabilityTests
{
    [Fact]
    public void HandoffButtonsExplainWhyAnEmptyListHasNoWordsToSend()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var fake = new FakeCommandClient();
            var selection = new SelectionViewModel(fake);
            var window = new MainWindow();
            var workspace = new WorkspaceShellViewModel(
                new ProjectViewModel(fake, new NoProjectPicker()),
                new BaselineViewModel(fake),
                selection,
                new AssessViewModel(fake, selection),
                new NoFolderPicker(), new NoDragSource(), fake,
                clipboard: new AvaloniaClipboard(window));
            window.Compose(workspace);
            workspace.Context.OpenTexts(TextsTab.Lists);
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                var wholeList = FindButton(window, "AI Handoff for the whole selected list");
                var checkedWords = FindButton(window, "AI Handoff for ticked words in the selected list");

                Assert.False(wholeList.IsEffectivelyEnabled);
                Assert.Equal("No words in this list to send to AI Handoff.",
                    AutomationProperties.GetHelpText(wholeList));
                Assert.False(checkedWords.IsEffectivelyEnabled);
                Assert.Equal("Tick words in this list before starting an AI Handoff.",
                    AutomationProperties.GetHelpText(checkedWords));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == AutomationProperties.GetHelpText(wholeList));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == AutomationProperties.GetHelpText(checkedWords));
            }
            finally
            {
                window.Close();
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    private static Button FindButton(MainWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));

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
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(
            Avalonia.Input.PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            Avalonia.Input.DragDropEffects allowedEffects) => Task.FromResult(Avalonia.Input.DragDropEffects.None);
    }
}
