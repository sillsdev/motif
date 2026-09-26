using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SIL.Motif.App.Services;

/// <summary>
/// Wraps one window's <see cref="TopLevel.Launcher"/> behind <see cref="IUriLauncher"/>, so no view model needs
/// to name <see cref="TopLevel"/> or <see cref="ILauncher"/>.
/// </summary>
public sealed class AvaloniaLauncher : IUriLauncher
{
    private readonly TopLevel _topLevel;

    public AvaloniaLauncher(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        _topLevel = topLevel;
    }

    public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        return _topLevel.Launcher.LaunchUriAsync(uri);
    }
}
