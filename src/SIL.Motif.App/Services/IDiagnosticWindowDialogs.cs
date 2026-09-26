using Avalonia.Controls;

namespace SIL.Motif.App.Services;

/// <summary>
/// Gives each window that hosts diagnostic tools open and save dialogs of its own, so a dialog started from a
/// diagnostic window belongs to that window rather than to the main one.
/// </summary>
/// <remarks>
/// A view calls this with the window it has just built, as it calls <see cref="IFileDragSource"/> with the
/// gesture it received. A view model only carries it on to the next window and never calls it, so
/// <see cref="IDiagnosticFilePicker"/>, the seam a view model does call, names no Avalonia type.
/// </remarks>
public interface IDiagnosticWindowDialogs
{
    /// <summary>The open and save dialogs that belong to <paramref name="window"/>.</summary>
    IDiagnosticFilePicker For(TopLevel window);
}
