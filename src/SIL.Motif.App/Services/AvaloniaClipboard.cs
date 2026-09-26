using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace SIL.Motif.App.Services;

/// <summary>
/// Wraps one window's <see cref="TopLevel.Clipboard"/> behind <see cref="IClipboard"/>, so no view model needs
/// to name <see cref="TopLevel"/> or Avalonia's own clipboard type. A window with no clipboard copies nothing.
/// </summary>
public sealed class AvaloniaClipboard : IClipboard
{
    private readonly TopLevel _topLevel;

    public AvaloniaClipboard(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        _topLevel = topLevel;
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (_topLevel.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
