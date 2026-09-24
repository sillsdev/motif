using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace SIL.Motif.App.Services;

/// <summary>
/// Wraps one window's <see cref="TopLevel"/> — its storage provider and its drag-and-drop entry point —
/// behind <see cref="IProjectPicker"/>, <see cref="IHandoffFolderPicker"/>, and <see cref="IFileDragSource"/>,
/// so no view model needs to name <see cref="TopLevel"/>, <see cref="IStorageProvider"/>, or
/// <see cref="IDataTransfer"/>.
/// </summary>
public sealed class AvaloniaStoragePickers : IProjectPicker, IHandoffFolderPicker, IFileDragSource
{
    private static readonly FilePickerFileType FwDataFileType =
        new("FieldWorks project") { Patterns = ["*.fwdata"] };

    private readonly TopLevel _topLevel;

    public AvaloniaStoragePickers(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        _topLevel = topLevel;
    }

    public async Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a FieldWorks project",
            AllowMultiple = false,
            FileTypeFilter = [FwDataFileType],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>The title of the dialog that chooses where the AI Handoff files are written.</summary>
    public const string FolderPickerTitle = "Choose where to write the AI Handoff";

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = FolderPickerTitle,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<DragDropEffects> StartDragAsync(
        PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(filePaths);

        var transfer = new DataTransfer();
        foreach (var path in filePaths)
        {
            var file = await _topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            if (file is not null) transfer.Add(DataTransferItem.CreateFile(file));
        }
        return await DragDrop.DoDragDropAsync(trigger, transfer, allowedEffects);
    }
}
