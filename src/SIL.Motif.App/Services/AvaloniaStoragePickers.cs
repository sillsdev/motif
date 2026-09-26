using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace SIL.Motif.App.Services;

/// <summary>
/// Wraps one window's <see cref="TopLevel"/> — its storage provider and its drag-and-drop entry point —
/// behind <see cref="IProjectPicker"/>, <see cref="IHandoffFolderPicker"/>, <see cref="IDiagnosticFilePicker"/>,
/// and <see cref="IFileDragSource"/>, so no view model needs to name <see cref="TopLevel"/>,
/// <see cref="IStorageProvider"/>, <see cref="IStorageFile"/>, or <see cref="IDataTransfer"/>. As
/// <see cref="IDiagnosticWindowDialogs"/> it gives another window dialogs of its own.
/// </summary>
public sealed class AvaloniaStoragePickers :
    IProjectPicker, IHandoffFolderPicker, IDiagnosticFilePicker, IDiagnosticWindowDialogs, IFileDragSource
{
    private static readonly FilePickerFileType FwDataFileType =
        new("FieldWorks project") { Patterns = ["*.fwdata"] };

    private static readonly FilePickerFileType DiagnosticFileType =
        new("Motif diagnostic JSON") { Patterns = ["*.json"] };

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

    public IDiagnosticFilePicker For(TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return ReferenceEquals(window, _topLevel) ? this : new AvaloniaStoragePickers(window);
    }

    public async Task<string?> OpenDiagnosticAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open diagnostic JSON",
            AllowMultiple = false,
            FileTypeFilter = [DiagnosticFileType],
        });
        if (files.Count == 0) return null;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task<bool> SaveDiagnosticAsync(
        string suggestedFileName, string json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        cancellationToken.ThrowIfCancellationRequested();
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diagnostic JSON",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [DiagnosticFileType],
        });
        if (file is null) return false;

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(json.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        return true;
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
