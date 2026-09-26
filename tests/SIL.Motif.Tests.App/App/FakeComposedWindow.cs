using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;

namespace SIL.Motif.Tests.App;

internal static class FakeComposedWindow
{
    public static void Click(MainWindow window, string name) =>
        HeadlessClick.Click(window, FindButton(window, name), name);

    public static Button FindButton(MainWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(button =>
            string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal));

    public static (WorkspaceShellViewModel Workspace, MainWindow Window) Create(FakeCommandClient? fake = null)
    {
        fake ??= new FakeCommandClient();
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
        return (workspace, window);
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
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(
            Avalonia.Input.PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            Avalonia.Input.DragDropEffects allowedEffects) => Task.FromResult(Avalonia.Input.DragDropEffects.None);
    }
}
