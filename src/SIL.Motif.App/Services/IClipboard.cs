namespace SIL.Motif.App.Services;

/// <summary>Lets a view model put text on the clipboard without depending on Avalonia's clipboard.</summary>
public interface IClipboard
{
    /// <summary>Replaces what the clipboard holds with <paramref name="text"/>.</summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
