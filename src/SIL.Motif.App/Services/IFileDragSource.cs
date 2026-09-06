using Avalonia.Input;

namespace SIL.Motif.App.Services;

/// <summary>
/// Starts a native drag of files that already exist on disk. A view's code-behind calls this with the
/// pointer gesture that began the drag; a view model exposes only the file paths and never calls it.
/// </summary>
public interface IFileDragSource
{
    Task<DragDropEffects> StartDragAsync(
        PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects);
}
