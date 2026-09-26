using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App;

internal static class FakeComposedWindow
{
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
